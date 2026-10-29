using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Pure document planning. Layout selects destinations; format capabilities serialize only declared document edits.
/// Execution, version checks, sidecars, checkpoints, and saved-baseline acknowledgment belong to the save coordinator.
/// </summary>
public sealed class WorkspaceDocumentSavePlanner(WorkspaceDocumentRouter router, IEnumerable<ISaveStrategy> serializers)
{
    public WorkspaceDocumentSavePlan Plan(WorkspaceSource source, WorkspaceDocumentSnapshot snapshot,
        IEnumerable<WorkspaceEntryDestination>? destinations = null, IEnumerable<DocumentId>? deletedDocuments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.GetFullPath(snapshot.Root) != Path.GetFullPath(source.Root))
            throw new InvalidOperationException("Snapshot and source belong to different workspace roots.");
        var before = source.Units.ToDictionary(u => (u.EntryId, u.Language));
        var current = snapshot.Units.ToDictionary(u => (u.EntryId, u.Language));
        var explicitDestinations = (destinations ?? []).ToDictionary(d => (d.EntryId, d.Language));
        if (explicitDestinations.Keys.Any(k => !current.ContainsKey(k)))
            throw new InvalidOperationException("A destination refers to an entry absent from the snapshot.");
        // Destinations chosen while editing travel with the captured units; caller-supplied destinations win.
        foreach (var unit in snapshot.Units.Where(u => u.Destination is not null))
            explicitDestinations.TryAdd((unit.EntryId, unit.Language), new(unit.EntryId, unit.Language, unit.Destination!));
        var documents = source.Documents.ToDictionary(d => d.Id);
        if (source.Documents.Any(d => Path.IsPathRooted(d.RelativePath) || Normalize(source.Root, d.RelativePath) != d.RelativePath))
            throw new InvalidOperationException("Source documents must use canonical workspace-relative paths.");
        var byPath = source.Documents.ToDictionary(d => Normalize(source.Root, d.RelativePath), StringComparer.Ordinal);
        var deletions = (deletedDocuments ?? []).Concat(snapshot.DeletedDocuments).ToHashSet();
        if (deletions.Any(id => !documents.ContainsKey(id))) throw new InvalidOperationException("Cannot delete an unknown document.");
        var edits = new Dictionary<DocumentId, List<DocumentTranslationEdit>>();
        var plannedUnits = new List<WorkspaceSourceUnit>();
        var addresses = new HashSet<(DocumentId, TranslationPath)>();

        void Edit(DocumentId id, DocumentTranslationEdit edit)
        {
            if (!edits.TryGetValue(id, out var list)) edits.Add(id, list = []);
            list.Add(edit);
        }

        foreach (var old in source.Units)
        {
            if (!documents.TryGetValue(old.Binding.DocumentId, out var document) || document.Language != old.Language)
                throw new InvalidOperationException("Source binding refers to an unknown or incompatible document.");
            if (!current.ContainsKey((old.EntryId, old.Language))) Edit(document.Id, new(old.Binding.Location, null));
        }
        foreach (var unit in snapshot.Units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (unit.EntryId.Value == Guid.Empty || string.IsNullOrWhiteSpace(unit.Language) || unit.KeyPath.Segments.Count == 0)
                throw new InvalidOperationException("A translation must have an identity, language, and key path.");
            before.TryGetValue((unit.EntryId, unit.Language), out var old);
            SourceDocument document;
            var originalDocument = old is null ? null : documents[old.Binding.DocumentId];
            if (old is not null && unit.Binding != old.Binding)
                throw new InvalidOperationException("Original source binding changed. Declare moves with an explicit destination.");
            if (explicitDestinations.TryGetValue((unit.EntryId, unit.Language), out var destination))
            {
                var relative = Normalize(source.Root, destination.RelativePath);
                byPath.TryGetValue(relative, out var existing);
                var path = router.NewEntryPath(source, unit.Language, existing?.Package, destination.RelativePath);
                document = Destination(path, unit.Language);
            }
            else if (originalDocument is not null)
            {
                if (!unit.Namespace.Equals(old!.Namespace))
                    throw new InvalidOperationException("Changing the source namespace requires an explicit destination.");
                document = originalDocument;
            }
            else
            {
                if (unit.Binding is not null) throw new InvalidOperationException("A new entry cannot claim an original source binding.");
                var package = unit.Namespace.Segments.Count == 0 ? null : string.Join('.', unit.Namespace.Segments);
                var matches = source.Documents.Where(d => d.Language == unit.Language && d.Package == package).ToList();
                if (matches.Count != 1)
                    throw new InvalidOperationException("Select an explicit destination for a new or ambiguous document.");
                document = matches[0];
            }
            if (!unit.Namespace.Equals(router.DocumentNamespace(source, document)))
                throw new InvalidOperationException("Entry namespace does not match the destination's framework layout.");
            if (document.Language != unit.Language) throw new InvalidOperationException("Destination belongs to another language.");
            if (deletions.Contains(document.Id)) throw new InvalidOperationException("A deleted document still owns translations.");
            if (!addresses.Add((document.Id, unit.KeyPath))) throw new InvalidOperationException("Multiple entries target the same document location.");
            var binding = new SourceBinding(document.Id, unit.KeyPath);
            plannedUnits.Add(new(unit.EntryId, unit.Language, unit.Namespace, unit.KeyPath, unit.Value, binding, unit.RawScalar, unit.Metadata));
            var replacement = new DocumentTranslation(unit.KeyPath, unit.Value, unit.RawScalar);
            if (old is null) Edit(document.Id, new(null, replacement));
            else if (old.Binding.DocumentId != document.Id)
            {
                Edit(old.Binding.DocumentId, new(old.Binding.Location, null));
                Edit(document.Id, new(null, replacement));
            }
            else if (!old.Binding.Location.Equals(unit.KeyPath) || old.Value != unit.Value || old.RawScalar != unit.RawScalar)
                Edit(document.Id, new(old.Binding.Location, replacement));
        }

        var targets = new List<WorkspaceDocumentSaveTarget>();
        foreach (var document in documents.Values.OrderBy(d => d.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deletions.Contains(document.Id))
            {
                targets.Add(new(document.Id, document.RelativePath, document.FormatId, WorkspaceDocumentSaveOperation.Delete,
                    document.Version, null, document.TextFormat));
                continue;
            }
            if (!edits.TryGetValue(document.Id, out var changes)) continue;
            var serializer = serializers.SingleOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, document.FormatId))
                ?? throw new FormatUnavailableException(document.FormatId);
            if (serializer is not IDocumentSaveStrategy writer)
                throw new NotSupportedException($"Format '{document.FormatId}' does not support document-aware serialization.");
            var content = writer.SerializeDocument(document.Content, changes.AsReadOnly(), cancellationToken);
            var isNew = !source.Documents.Any(d => d.Id == document.Id);
            if (!isNew && content == document.Content) continue;
            targets.Add(new(document.Id, document.RelativePath, document.FormatId,
                isNew ? WorkspaceDocumentSaveOperation.Create : WorkspaceDocumentSaveOperation.Update,
                isNew ? null : document.Version, content, document.TextFormat));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var remainingDocuments = documents.Values.Where(d => !deletions.Contains(d.Id)).ToArray();
        var catalog = router.IdentityCatalog(source, remainingDocuments, plannedUnits);
        var artifacts = PlanArtifacts(source, plannedUnits, catalog, remainingDocuments, snapshot.Manifest);
        return new(source, snapshot, targets, plannedUnits, remainingDocuments, catalog, artifacts);

        SourceDocument Destination(string path, string language)
        {
            var relative = Normalize(source.Root, path);
            if (byPath.TryGetValue(relative, out var existing)) return existing;
            if (relative == ".toucan" || relative.StartsWith(".toucan/", StringComparison.Ordinal))
                throw new InvalidOperationException("Translation destinations cannot use the workspace metadata folder.");
            var description = router.DescribeNewDocument(source, path, language);
            var document = new SourceDocument(WorkspaceSourceLoader.DocumentIdentity(relative), relative, language, description.Package,
                source.FormatId, string.Empty, "{\n}\n");
            byPath.Add(relative, document);
            documents.Add(document.Id, document);
            return document;
        }
    }

    private static IReadOnlyList<WorkspaceArtifactSaveTarget> PlanArtifacts(WorkspaceSource source, IReadOnlyList<WorkspaceSourceUnit> units,
        IReadOnlyList<WorkspaceEntryIdentity> catalog, IReadOnlyList<SourceDocument> documents, string? manifest)
    {
        var result = new List<WorkspaceArtifactSaveTarget>();
        var previous = source.Artifacts.ToDictionary(a => a.RelativePath, StringComparer.Ordinal);
        void Target(string path, string? content)
        {
            previous.TryGetValue(path, out var old);
            if (old is null && content is null) return;
            result.Add(new(path, content is null ? WorkspaceDocumentSaveOperation.Delete : old is null
                ? WorkspaceDocumentSaveOperation.Create : WorkspaceDocumentSaveOperation.Update, old?.Version, content, old?.TextFormat ?? SourceTextFormat.Utf8));
        }
        if (manifest is not null && previous.TryGetValue("toucan.tproj", out var originalManifest)
            && ProjectSettings.NormalizeManifest(manifest) != ProjectSettings.NormalizeManifest(originalManifest.Content))
            Target("toucan.tproj", manifest);
        var identity = WorkspaceMetadataCodec.WriteIdentities(source.FrameworkId, source.FormatId, catalog);
        var before = WorkspaceMetadataCodec.WriteIdentities(source.FrameworkId, source.FormatId, source.IdentityCatalog);
        if (identity != before || !previous.ContainsKey(WorkspaceMetadataCodec.IdentityPath)
            && catalog.Any(e => e.Id != WorkspaceSourceLoader.EntryIdentity(e.Resource, e.Namespace, e.KeyPath)))
            Target(WorkspaceMetadataCodec.IdentityPath, identity);
        foreach (var id in source.Documents.Select(d => d.Id).Concat(documents.Select(d => d.Id)).Distinct())
        {
            var current = units.Where(u => u.Binding.DocumentId == id && u.Metadata is { IsEmpty: false }).Select(u => u.Metadata!).ToArray();
            var baseline = source.Units.Where(u => u.Binding.DocumentId == id && u.Metadata is { IsEmpty: false }).Select(u => u.Metadata!).ToArray();
            if (WorkspaceMetadataCodec.WriteEntries(id, current) != WorkspaceMetadataCodec.WriteEntries(id, baseline) || current.Length > 0 && !previous.ContainsKey(WorkspaceMetadataCodec.EntryPath(id)))
                Target(WorkspaceMetadataCodec.EntryPath(id), current.Length == 0 ? null : WorkspaceMetadataCodec.WriteEntries(id, current));
        }
        return result;
    }

    private static string Normalize(string root, string path) =>
        Path.GetRelativePath(root, WorkspaceDocumentRouter.WithinRoot(root, path)).Replace(Path.DirectorySeparatorChar, '/');
}
