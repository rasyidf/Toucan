using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Loads document-aware state from registered capabilities. Stateless: the caller owns workspace/connection
/// scopes, watches, and plugin activation. Construction and registration perform no source I/O.
/// </summary>
public sealed class WorkspaceSourceLoader(IEnumerable<IFrameworkProfile> profiles, IEnumerable<ILoadStrategy> loaders, IWorkspaceSourceReader? sourceReader = null)
{
    public async Task<WorkspaceSource> LoadAsync(string root, string frameworkId, string? formatId = null,
        WorkspaceIdentityCheckpoint? identityCheckpoint = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(frameworkId);
        cancellationToken.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Workspace folder '{root}' does not exist.");
        if (identityCheckpoint is not null && !string.Equals(identityCheckpoint.Root, root, StringComparison.Ordinal))
            throw new InvalidOperationException("Identity checkpoint belongs to a different source root.");
        var profile = profiles.SingleOrDefault(p => p.Id == frameworkId)
            ?? throw new InvalidOperationException($"Framework '{frameworkId}' is not available.");
        var format = formatId ?? profile.DefaultFormatId;
        var loader = loaders.SingleOrDefault(l => l is not IManifestLoadStrategy && FormatIds.Comparer.Equals(l.FormatId, format))
            ?? throw new FormatUnavailableException(format);
        if (loader is not IDocumentLoadStrategy reader)
            throw new NotSupportedException($"Format '{format}' does not support document-aware loading.");

        IWorkspaceSourceReader access = sourceReader ?? new LocalWorkspaceSourceReader();
        var artifacts = new List<WorkspaceSourceArtifact>();
        var persisted = await access.ReadOptionalAsync(root, WorkspaceMetadataCodec.IdentityPath, cancellationToken).ConfigureAwait(false);
        if (persisted is not null) artifacts.Add(new(WorkspaceMetadataCodec.IdentityPath, persisted.Version, persisted.Content, persisted.TextFormat));
        var checkpointEntries = identityCheckpoint?.Entries ?? (persisted is null ? [] : WorkspaceMetadataCodec.ReadIdentities(persisted.Content, frameworkId, format));
        var manifest = await access.ReadOptionalAsync(root, "toucan.tproj", cancellationToken).ConfigureAwait(false);
        if (manifest is not null) artifacts.Add(new("toucan.tproj", manifest.Version, manifest.Content, manifest.TextFormat));
        var documents = new List<SourceDocument>();
        var units = new List<WorkspaceSourceUnit>();
        var savedIdentities = checkpointEntries.ToDictionary(i => (i.Resource, i.Namespace, i.KeyPath), i => i.Id);
        var loadedIdentities = new Dictionary<(string, TranslationPath, TranslationPath), WorkspaceEntryIdentity>();
        var seenDocuments = new HashSet<DocumentId>();
        var seenUnits = new HashSet<(EntryId, string)>();
        foreach (var file in profile.DiscoverFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolute = Path.GetFullPath(file.AbsolutePath);
            var relative = Path.GetRelativePath(root, absolute).Replace(Path.DirectorySeparatorChar, '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidOperationException($"Discovered document '{absolute}' is outside the workspace.");
            if (string.IsNullOrWhiteSpace(file.Language))
                throw new InvalidOperationException($"Discovered document '{relative}' has no language.");
            var documentId = DocumentIdentity(relative);
            if (!seenDocuments.Add(documentId))
                throw new InvalidOperationException($"Framework discovered '{relative}' more than once.");
            var read = await access.ReadAsync(root, relative, cancellationToken).ConfigureAwait(false);
            var content = read.Content;
            var document = new SourceDocument(documentId, relative, file.Language, file.Package, format,
                read.Version, content) { TextFormat = read.TextFormat };
            documents.Add(document);
            var normalized = file with { AbsolutePath = absolute, RelativePath = relative };
            var layout = profile is IFrameworkDocumentLayout convention
                ? convention.GetDocumentLayout(normalized)
                : new FrameworkDocumentLayout(null, file.Package is null ? TranslationPath.Empty : new([file.Package]));
            foreach (var parsed in reader.LoadDocument(content, cancellationToken))
            {
                // Only an explicit profile family groups translations across documents/languages.
                var resource = layout.Family is null
                    ? JsonSerializer.Serialize(new[] { "document", relative })
                    : JsonSerializer.Serialize(new[] { "family", frameworkId, layout.Family });
                var address = (resource, layout.Namespace, parsed.KeyPath);
                var id = savedIdentities.TryGetValue(address, out var saved)
                    ? saved
                    : EntryIdentity(resource, layout.Namespace, parsed.KeyPath);
                if (id.Value == Guid.Empty) throw new InvalidOperationException("Entry identity cannot be empty.");
                loadedIdentities.TryAdd(address, new(resource, layout.Namespace, parsed.KeyPath, id));
                if (!seenUnits.Add((id, file.Language)))
                    throw new InvalidOperationException($"Ambiguous resource family for '{relative}' at '{parsed.KeyPath.EscapedPath}'.");
                units.Add(new(id, file.Language, layout.Namespace, parsed.KeyPath, parsed.Value,
                    new(documentId, parsed.KeyPath), parsed.RawScalar));
            }
        }
        var byUnit = units.Select((unit, index) => (unit, index)).ToDictionary(p => (p.unit.EntryId, p.unit.Language));
        foreach (var document in documents)
        {
            var path = WorkspaceMetadataCodec.EntryPath(document.Id);
            var metadata = await access.ReadOptionalAsync(root, path, cancellationToken).ConfigureAwait(false);
            if (metadata is null) continue;
            artifacts.Add(new(path, metadata.Version, metadata.Content, metadata.TextFormat));
            var seen = new HashSet<(EntryId, string)>();
            foreach (var entry in WorkspaceMetadataCodec.ReadEntries(metadata.Content, document.Id))
            {
                if (!seen.Add((entry.EntryId, entry.Language))) throw new JsonException("Duplicate entry metadata.");
                if (byUnit.TryGetValue((entry.EntryId, entry.Language), out var found) && found.unit.Binding.DocumentId == document.Id)
                    units[found.index] = found.unit with { Metadata = entry.IsEmpty ? null : entry };
                // Metadata for externally removed entries is retained in the source artifact until an explicit save changes this document.
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new WorkspaceSource(root, frameworkId, format, documents, units, loadedIdentities.Values, artifacts);
    }

    internal static EntryId EntryIdentity(string resource, TranslationPath ns, TranslationPath key) => new(Identity(resource, ns.EscapedPath, key.EscapedPath));

    internal static DocumentId DocumentIdentity(string relativePath) => new(Identity("document", relativePath));

    private static Guid Identity(params string[] parts) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))).AsSpan(0, 16));
}
