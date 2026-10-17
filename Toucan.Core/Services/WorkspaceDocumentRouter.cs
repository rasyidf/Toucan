using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Resolves ownership before serialization; never opens or writes a file.</summary>
public sealed class WorkspaceDocumentRouter(IEnumerable<IFrameworkProfile> profiles)
{
    public string ExistingPath(WorkspaceSource source, WorkspaceSourceUnit unit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(unit);
        var document = source.Documents.Single(d => d.Id == unit.Binding.DocumentId);
        return WithinRoot(source.Root, document.RelativePath);
    }

    /// <summary>
    /// Selects an existing destination when unique, otherwise asks the framework for a new document path.
    /// An explicit path resolves layout ambiguity. Existing units must use ExistingPath instead.
    /// </summary>
    public string NewEntryPath(WorkspaceSource source, string language, string? package = null, string? relativeDestination = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (relativeDestination is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativeDestination);
            if (Path.IsPathRooted(relativeDestination)) throw new ArgumentException("Destination must be relative.", nameof(relativeDestination));
            var explicitPath = WithinRoot(source.Root, relativeDestination);
            if (source.Documents.Any(d => WithinRoot(source.Root, d.RelativePath) == explicitPath && (d.Language != language || d.Package != package)))
                throw new InvalidOperationException("Destination already belongs to another language or package.");
            return explicitPath;
        }
        var matches = source.Documents.Where(d => d.Language == language && d.Package == package).ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException("More than one destination matches. Select a document explicitly.");
        if (matches.Count == 1) return WithinRoot(source.Root, matches[0].RelativePath);
        if (package is not null && source.Documents.Count > 0 && !source.Documents.Any(d => d.Package == package))
            throw new InvalidOperationException("Select an explicit destination for a new package.");

        var profile = profiles.SingleOrDefault(p => p.Id == source.FrameworkId)
            ?? throw new InvalidOperationException($"Framework '{source.FrameworkId}' is not available.");
        var proposed = profile.GetFilePath(source.Root, language, package);
        var path = WithinRoot(source.Root, proposed);
        if (profile is IFrameworkDocumentLayout layout)
        {
            var families = source.Documents.Where(d => d.Package == package)
                .Select(d => layout.GetDocumentLayout(new(WithinRoot(source.Root, d.RelativePath), d.RelativePath, d.Language, d.Package)).Family)
                .Distinct(StringComparer.Ordinal).ToList();
            var candidate = layout.GetDocumentLayout(new(path, Path.GetRelativePath(source.Root, path).Replace(Path.DirectorySeparatorChar, '/'), language, package));
            if (families.Count > 1 || families.Count == 1 && families[0] != candidate.Family)
                throw new InvalidOperationException("Framework defaults do not uniquely preserve the discovered layout. Select a destination explicitly.");
        }
        else if (source.Documents.Any(d => d.Package == package))
        {
            throw new InvalidOperationException("The framework does not declare how language documents are related. Select a destination explicitly.");
        }
        if (source.Documents.Any(d => WithinRoot(source.Root, d.RelativePath) == path))
            throw new InvalidOperationException("The proposed destination already belongs to another resource.");
        return path;
    }

    internal IReadOnlyList<WorkspaceEntryIdentity> IdentityCatalog(WorkspaceSource source, IEnumerable<SourceDocument> documents,
        IEnumerable<WorkspaceSourceUnit> units)
    {
        var profile = profiles.SingleOrDefault(p => p.Id == source.FrameworkId)
            ?? throw new InvalidOperationException($"Framework '{source.FrameworkId}' is not available.");
        var resources = documents.ToDictionary(d => d.Id, d =>
        {
            var family = profile is IFrameworkDocumentLayout layout
                ? layout.GetDocumentLayout(new(WithinRoot(source.Root, d.RelativePath), d.RelativePath, d.Language, d.Package)).Family : null;
            return System.Text.Json.JsonSerializer.Serialize(family is null
                ? new[] { "document", d.RelativePath } : new[] { "family", source.FrameworkId, family });
        });
        var catalog = new Dictionary<(string, TranslationPath, TranslationPath), WorkspaceEntryIdentity>();
        foreach (var unit in units)
        {
            var address = (resources[unit.Binding.DocumentId], unit.Namespace, unit.KeyPath);
            if (catalog.TryGetValue(address, out var existing) && existing.Id != unit.EntryId)
                throw new InvalidOperationException("Destination language variants disagree on their entry identity.");
            catalog[address] = new(address.Item1, unit.Namespace, unit.KeyPath, unit.EntryId);
        }
        return catalog.Values.ToArray();
    }

    internal DiscoveredFile DescribeNewDocument(WorkspaceSource source, string path, string language)
    {
        var relative = Path.GetRelativePath(source.Root, WithinRoot(source.Root, path)).Replace(Path.DirectorySeparatorChar, '/');
        var profile = profiles.SingleOrDefault(p => p.Id == source.FrameworkId)
            ?? throw new InvalidOperationException($"Framework '{source.FrameworkId}' is not available.");
        if (profile is not IFrameworkDocumentLayout layout || layout.DescribeDestination(source.Root, relative) is not { } description)
            throw new NotSupportedException("The framework cannot describe how this destination will be discovered.");
        if (description.Language != language || WithinRoot(source.Root, description.AbsolutePath) != WithinRoot(source.Root, relative)
            || description.RelativePath.Replace(Path.DirectorySeparatorChar, '/') != relative)
            throw new InvalidOperationException("Destination does not match the framework's language or path conventions.");
        return description;
    }

    internal TranslationPath DocumentNamespace(WorkspaceSource source, SourceDocument document)
    {
        var profile = profiles.SingleOrDefault(p => p.Id == source.FrameworkId)
            ?? throw new InvalidOperationException($"Framework '{source.FrameworkId}' is not available.");
        return profile is IFrameworkDocumentLayout layout
            ? layout.GetDocumentLayout(new(WithinRoot(source.Root, document.RelativePath), document.RelativePath, document.Language, document.Package)).Namespace
            : document.Package is null ? TranslationPath.Empty : new([document.Package]);
    }

    internal static string WithinRoot(string root, string path)
    {
        var absolute = Path.GetFullPath(path, root);
        var relative = Path.GetRelativePath(root, absolute);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Destination is outside the workspace.");
        return absolute;
    }
}
