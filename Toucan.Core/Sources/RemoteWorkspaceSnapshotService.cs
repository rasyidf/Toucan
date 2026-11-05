using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>What a refresh changed in a cache.</summary>
public sealed record RemoteSnapshotRefresh(IReadOnlyList<string> Added, IReadOnlyList<string> Updated, IReadOnlyList<string> Removed, int Unchanged,
    DateTimeOffset RefreshedAt)
{
    public bool HasChanges => Added.Count + Updated.Count + Removed.Count > 0;
}

/// <summary>A save plan checked against the document source, with nothing written.</summary>
/// <param name="Stale">Targets whose document at the source no longer matches the version the plan was made from.</param>
/// <param name="Refusal">Why the save cannot run (a read-only source), or null. Nothing is sent to the source in that case.</param>
public sealed record RemoteSavePreview(WorkspaceDocumentSavePlan Plan, IReadOnlyList<string> Creates, IReadOnlyList<string> Updates,
    IReadOnlyList<string> Deletes, IReadOnlyList<string> Stale, string? Refusal = null)
{
    public bool HasChanges => Creates.Count + Updates.Count + Deletes.Count > 0;
    public bool CanSave => Refusal is null && Stale.Count == 0;
}

/// <summary>
/// Snapshot loading for a connection whose service is primary (role <c>Primary</c>): a document source is pulled into a local
/// cache, the project opens from the cache with the ordinary loader, and later opens need no network until the user asks for
/// a refresh. The cache is a plain folder of the service's files plus a manifest that records each file's service version.
/// </summary>
/// <remarks>
/// A refresh changes the cache only after the service listed every document: an incomplete or duplicated listing fails the
/// refresh and deletes nothing, so a dropped page never looks like a remote deletion.
/// </remarks>
public sealed class RemoteWorkspaceSnapshotService(WorkspaceSourceLoader loader, WorkspaceDocumentSavePlanner planner)
{
    private const string ManifestName = "cache.json";
    private const string FilesName = "files";

    /// <summary>The folder a cached project opens from.</summary>
    public static string FilesRoot(string cacheRoot) => Path.Combine(cacheRoot, FilesName);

    /// <summary>Whether a refresh has completed here before, so the project can open offline.</summary>
    public static bool IsCached(string cacheRoot) => File.Exists(Path.Combine(cacheRoot, ManifestName));

    /// <summary>When the cache was last refreshed, or null when it never was.</summary>
    public static DateTimeOffset? LastRefreshed(string cacheRoot) => ReadManifest(cacheRoot)?.RefreshedAt;

    /// <summary>Pulls every document the source lists into the cache, downloading only what changed.</summary>
    /// <param name="cacheRoot">The connection's cache folder; created on first use.</param>
    /// <param name="prefix">Only documents under this path, or null for the whole connection.</param>
    public async Task<RemoteSnapshotRefresh> RefreshAsync(IDocumentSourceSession session, string cacheRoot, string? prefix = null,
        TimeProvider? time = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        var listing = await ListAsync(session, prefix, cancellationToken).ConfigureAwait(false);
        var manifest = ReadManifest(cacheRoot) ?? new Manifest();
        var files = FilesRoot(cacheRoot);
        Directory.CreateDirectory(files);

        var added = new List<string>();
        var updated = new List<string>();
        var unchanged = 0;
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, info) in listing.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var local = Resolve(files, path);
            var known = manifest.Documents.GetValueOrDefault(path);
            var existed = File.Exists(local);
            if (known == info.Version && existed)
            {
                next[path] = info.Version;
                unchanged++;
                continue;
            }
            var content = await session.ReadDocumentAsync(info.Ref, cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteAllBytesAsync(local, content.Bytes.ToArray()).ConfigureAwait(false);
            next[path] = content.Version;
            (existed ? updated : added).Add(path);
        }

        // The listing was complete, so what it omits was removed at the source.
        var removed = new List<string>();
        foreach (var path in manifest.Documents.Keys.Where(p => !listing.ContainsKey(p)).Order(StringComparer.Ordinal))
        {
            var local = Resolve(files, path);
            if (File.Exists(local)) File.Delete(local);
            PruneEmptyDirectories(files, Path.GetDirectoryName(local));
            removed.Add(path);
        }

        var refreshedAt = (time ?? TimeProvider.System).GetUtcNow();
        await AtomicFile.WriteAllTextAsync(Path.Combine(cacheRoot, ManifestName),
            JsonSerializer.Serialize(new Manifest { RefreshedAt = refreshedAt, Documents = next }, s_json)).ConfigureAwait(false);
        return new(added, updated, removed, unchanged, refreshedAt);
    }

    /// <summary>Opens the cached project without contacting the source.</summary>
    public Task<WorkspaceSource> OpenCachedAsync(string cacheRoot, string frameworkId, string? formatId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        if (!IsCached(cacheRoot)) throw new InvalidOperationException("Nothing is cached yet; refresh from the source first.");
        return loader.LoadAsync(FilesRoot(cacheRoot), frameworkId, formatId, cancellationToken: cancellationToken);
    }

    /// <summary>Opens the cache, refreshing it first when it is empty or <paramref name="refresh"/> is true.</summary>
    public async Task<WorkspaceSource> LoadAsync(IDocumentSourceSession session, string cacheRoot, string frameworkId, string? formatId = null,
        bool refresh = false, string? prefix = null, CancellationToken cancellationToken = default)
    {
        if (refresh || !IsCached(cacheRoot))
            await RefreshAsync(session, cacheRoot, prefix, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await OpenCachedAsync(cacheRoot, frameworkId, formatId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Plans a save of <paramref name="snapshot"/> over <paramref name="source"/> and checks each target against the source.
    /// Nothing is written.
    /// </summary>
    public async Task<RemoteSavePreview> PreviewSaveAsync(IDocumentSourceSession session, WorkspaceSource source, WorkspaceDocumentSnapshot snapshot,
        IEnumerable<WorkspaceEntryDestination>? destinations = null, IEnumerable<DocumentId>? deletedDocuments = null,
        SourceCapabilities capabilities = RemoteWorkspaceDocumentStore.ReadWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var plan = planner.Plan(source, snapshot, destinations, deletedDocuments, cancellationToken);
        var store = new RemoteWorkspaceDocumentStore(session, capabilities);
        var creates = new List<string>();
        var updates = new List<string>();
        var deletes = new List<string>();
        var stale = new List<string>();
        var targets = plan.Targets.Select(t => (t.RelativePath, t.Operation, t.ExpectedVersion))
            .Concat(plan.Artifacts.Select(a => (a.RelativePath, a.Operation, a.ExpectedVersion)));
        string? refusal = null;
        foreach (var (path, operation, expected) in targets)
        {
            (operation switch { WorkspaceDocumentSaveOperation.Create => creates, WorkspaceDocumentSaveOperation.Delete => deletes, _ => updates }).Add(path);
            if (!capabilities.HasFlag(SourceCapabilities.WriteDocuments))
            {
                refusal = "This source is read-only, so these changes cannot be saved to it.";
                continue;
            }
            var actual = await store.VersionAsync(source.Root, path, cancellationToken).ConfigureAwait(false);
            if (actual != expected) stale.Add(path);
        }
        return new(plan, creates, updates, deletes, stale, refusal);
    }

    private static async Task<Dictionary<string, RemoteDocumentInfo>> ListAsync(IDocumentSourceSession session, string? prefix, CancellationToken cancellationToken)
    {
        var all = new Dictionary<string, RemoteDocumentInfo>(StringComparer.Ordinal);
        var complete = false;
        await foreach (var page in session.ListDocumentsAsync(new(prefix), cancellationToken).ConfigureAwait(false))
        {
            if (complete) throw Incomplete("The source listed documents after its last page.");
            foreach (var info in page.Documents)
            {
                var path = RemoteWorkspaceDocumentStore.RemotePath(info.Ref.Path);
                if (!all.TryAdd(path, info with { Ref = info.Ref with { Path = path } }))
                    throw new SourceException(new SourceError(SourceErrorKind.Validation, $"The source lists '{path}' more than once."));
            }
            complete = page.IsComplete;
        }
        if (!complete) throw Incomplete("The source did not finish listing its documents.");
        return all;

        static SourceException Incomplete(string message) =>
            new(new SourceError(SourceErrorKind.Unavailable, message + " The cache was left as it was."));
    }

    private static string Resolve(string files, string path)
    {
        var absolute = Path.GetFullPath(path, files);
        var relative = Path.GetRelativePath(files, absolute);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new SourceException(new SourceError(SourceErrorKind.Validation, $"The source lists '{path}' outside the project."));
        return absolute;
    }

    private static void PruneEmptyDirectories(string files, string? directory)
    {
        var root = Path.GetFullPath(files);
        while (directory is not null && Path.GetFullPath(directory).Length > root.Length && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static Manifest? ReadManifest(string cacheRoot)
    {
        try { return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(cacheRoot, ManifestName)), s_json); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset RefreshedAt { get; set; }
        public Dictionary<string, string> Documents { get; set; } = new(StringComparer.Ordinal);
    }
}
