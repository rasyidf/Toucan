using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>What opening a project from a service made.</summary>
/// <param name="Label">How the recent list names it, for example <c>crowdin:my-project</c>.</param>
public sealed record RemoteProjectResult(string Folder, string Label, SourceConnectionSettings Connection, int Documents);

/// <summary>
/// Opens a project that exists only at a service (Crowdin, for example): creates a local project folder, writes its manifest
/// with a connection whose role is <c>primary</c> (the service is the main copy), and pulls every document into the folder.
/// The folder is an ordinary project afterwards; later pulls and pushes go through the Sources panel like any connection.
/// </summary>
public sealed class RemoteProjectBootstrap(Func<SourceConnectionSettings, string, CancellationToken, Task<IContentSourceSession>> openSession,
    Action<ProjectSettings, string>? detectFramework = null, TimeProvider? time = null)
{
    /// <summary>The label for the recent list: the service's name in lower case, a colon, the remote project's name.</summary>
    public static string LabelFor(RegisteredSourceAdapter adapter, string projectName) =>
        $"{adapter.Descriptor.DisplayName.ToLowerInvariant().Replace(' ', '-')}:{projectName}";

    /// <summary>A folder name that is safe on every platform, from a project name.</summary>
    public static string FolderName(string projectName)
    {
        // The same set everywhere, so a folder made on macOS can be copied to Windows.
        const string invalid = "\\/:*?\"<>|";
        var cleaned = new string([.. projectName.Select(c => invalid.Contains(c, StringComparison.Ordinal) || char.IsWhiteSpace(c) || char.IsControl(c) ? '-' : c)]).Trim('-', '.');
        return cleaned.Length == 0 ? "project" : cleaned;
    }

    /// <param name="baseUrl">The service address, or null for the adapter's default.</param>
    public async Task<RemoteProjectResult> CreateAsync(RegisteredSourceAdapter adapter, Uri? baseUrl, RemoteProjectSummary project, string folder,
        IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!adapter.Descriptor.Capabilities.HasFlag(SourceCapabilities.ReadDocuments))
            throw new NotSupportedException($"{adapter.Descriptor.DisplayName} stores translations as keys, not files. Add it as a connection to a project you have open instead.");
        folder = Path.GetFullPath(folder);
        if (File.Exists(Path.Combine(folder, "toucan.tproj")))
            throw new InvalidOperationException($"{folder} is already a Toucan project. Choose another folder, or open it from the recent list.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException($"{folder} is not empty. Choose an empty or new folder.");
        Directory.CreateDirectory(folder);

        var connection = new SourceConnectionSettings
        {
            Id = ConnectionIdFor(adapter),
            Name = project.Name,
            Adapter = adapter.Descriptor.Id,
            BaseUrl = baseUrl,
            RemoteProject = new SourceRemoteProject { Id = project.Id, Name = project.Name },
            Role = SourceRoles.Primary,
        };
        var session = await openSession(connection, folder, cancellationToken).ConfigureAwait(false);
        if (session is not IDocumentSourceSession files)
            throw new NotSupportedException($"{adapter.Descriptor.DisplayName} did not offer a file session for this project.");
        var info = await session.DescribeAsync(cancellationToken).ConfigureAwait(false);

        // Every language of the remote project becomes a language of the local one, under the service's own code.
        var codes = info.Languages.Select(l => l.Code).Distinct(StringComparer.Ordinal).ToList();
        foreach (var code in codes) connection.Languages[code] = code;
        var primary = info.SourceLanguage ?? info.Languages.FirstOrDefault(l => l.IsSource)?.Code ?? codes.FirstOrDefault() ?? "en";

        var settings = ProjectSettings.CreateDefault(folder, project.Name);
        settings.PrimaryLanguage = primary;
        settings.Languages = [.. codes];
        settings.Sources.Add(connection);
        settings.Save();

        // Pull everything: nothing exists locally yet, so every document is a new file and nothing can conflict.
        var store = new SyncStateStore(folder, connection.Id, time);
        var sync = new DocumentSyncService(time);
        var caps = adapter.Descriptor.Capabilities & info.Permissions;
        var plan = await sync.PlanAsync(connection.Id, connection, SyncPlanKind.Pull, files, caps, store, folder, [], 0, cancellationToken).ConfigureAwait(false);
        var (pulled, _) = await sync.ApplyAsync(plan, files, store, folder, progress, cancellationToken).ConfigureAwait(false);
        if (!pulled.Succeeded) throw new InvalidOperationException(pulled.Refusal ?? "Some documents could not be written.");

        // The files are in: work out how the project is laid out, and which of its languages is which of the service's.
        if (detectFramework is not null)
        {
            detectFramework(settings, folder);
            if (!string.IsNullOrWhiteSpace(settings.Framework))
            {
                connection.Languages.Clear();
                foreach (var suggestion in SourceMapping.SuggestLanguages(settings.Languages, info.Languages).Where(s => s.RemoteLanguage is not null))
                    connection.Languages[suggestion.ProjectLanguage] = suggestion.RemoteLanguage!;
            }
            settings.Save();
        }
        return new RemoteProjectResult(folder, LabelFor(adapter, project.Name), connection, pulled.Created + pulled.Applied);
    }

    /// <summary>The ID the connection gets in the new project (the service's name). Bind the account to it before calling <see cref="CreateAsync"/>.</summary>
    public static string ConnectionIdFor(RegisteredSourceAdapter adapter)
    {
        var stem = adapter.Descriptor.DisplayName.ToLowerInvariant();
        var id = new string([.. stem.Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-');
        return id.Length == 0 ? "service" : id;
    }
}
