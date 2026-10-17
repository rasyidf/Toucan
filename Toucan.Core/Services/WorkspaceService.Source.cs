using System.Collections.Immutable;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services;

public sealed partial class WorkspaceService
{
    public WorkspaceSourceRefreshResult ReloadSource(WorkspaceSource incoming, bool discardDirty = false)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        DocumentId[] accepted;
        DocumentId[] conflicts;
        lock (_gate)
        {
            if (_settings is null || _source is null || _closing || incoming.Root != _source.Root || incoming.FrameworkId != _source.FrameworkId
                || !FormatIds.Comparer.Equals(incoming.FormatId, _source.FormatId)) throw new InvalidOperationException("Source reload belongs to another or closed workspace.");
            var old = _source;
            var changed = old.Documents.Select(d => d.Id).Union(incoming.Documents.Select(d => d.Id)).Where(id =>
                old.Documents.SingleOrDefault(d => d.Id == id)?.Version != incoming.Documents.SingleOrDefault(d => d.Id == id)?.Version
                || ArtifactVersion(old, WorkspaceMetadataCodec.EntryPath(id)) != ArtifactVersion(incoming, WorkspaceMetadataCodec.EntryPath(id))).ToHashSet();
            var identityChanged = ArtifactVersion(old, WorkspaceMetadataCodec.IdentityPath) != ArtifactVersion(incoming, WorkspaceMetadataCodec.IdentityPath);
            if (identityChanged) changed.UnionWith(old.Documents.Select(d => d.Id).Concat(incoming.Documents.Select(d => d.Id)));
            var dirty = _state.Keys.Union(_baseline.Keys).Where(k => !_state.TryGetValue(k, out var a) || !_baseline.TryGetValue(k, out var b) || !Equivalent(a, b))
                .Select(k => _state.GetValueOrDefault(k)?.Binding?.DocumentId ?? _baseline.GetValueOrDefault(k)?.Binding?.DocumentId).ToArray();
            conflicts = discardDirty ? [] : changed.Where(id => dirty.Contains(id) || dirty.Contains(null) || identityChanged && dirty.Length > 0).ToArray();
            accepted = changed.Except(conflicts).ToArray();
            if (accepted.Length == 0) return new([], conflicts);
            var refreshed = incoming.Units.Where(u => accepted.Contains(u.Binding.DocumentId)).Select(FromSource).ToImmutableDictionary(u => (u.Id, u.Language));
            ImmutableDictionary<(Guid, string), Unit> Replace(ImmutableDictionary<(Guid, string), Unit> state)
            {
                var result = state.RemoveRange(state.Where(p => p.Value.Binding is { } binding && accepted.Contains(binding.DocumentId)).Select(p => p.Key));
                return result.SetItems(refreshed);
            }
            _state = Replace(_state); _baseline = Replace(_baseline);
            for (var node = _undo.First; node is not null;)
            {
                var next = node.Next; node.Value = (Replace(node.Value.Before), Replace(node.Value.After));
                if (Same(node.Value.Before, node.Value.After)) _undo.Remove(node); node = next;
            }
            var redo = _redo.ToArray(); _redo.Clear();
            foreach (var item in redo.Reverse()) { var before = Replace(item.Before); var after = Replace(item.After); if (!Same(before, after)) _redo.Push((before, after)); }
            var docs = old.Documents.Where(d => !accepted.Contains(d.Id)).Concat(incoming.Documents.Where(d => accepted.Contains(d.Id))).ToArray();
            var units = old.Units.Where(u => !accepted.Contains(u.Binding.DocumentId)).Concat(incoming.Units.Where(u => accepted.Contains(u.Binding.DocumentId))).ToArray();
            var paths = accepted.Select(WorkspaceMetadataCodec.EntryPath).ToHashSet(StringComparer.Ordinal);
            if (identityChanged) paths.Add(WorkspaceMetadataCodec.IdentityPath);
            var artifacts = old.Artifacts.Where(a => !paths.Contains(a.RelativePath)).Concat(incoming.Artifacts.Where(a => paths.Contains(a.RelativePath))).ToArray();
            var retainedIds = old.Units.Where(u => !accepted.Contains(u.Binding.DocumentId)).Select(u => u.EntryId).ToHashSet();
            var refreshedIds = incoming.Units.Where(u => accepted.Contains(u.Binding.DocumentId)).Select(u => u.EntryId).ToHashSet();
            var catalog = incoming.IdentityCatalog.Where(e => refreshedIds.Contains(e.Id)).Concat(old.IdentityCatalog.Where(e => retainedIds.Contains(e.Id))).Distinct().ToArray();
            _source = new(old.Root, old.FrameworkId, old.FormatId, docs, units, catalog, artifacts);
            _revision++; PublishProjections(); PublishChangeLocked(WorkspaceChangeKind.Reloaded, WorkspaceChangeOrigin.Source);
        }
        RaiseChanged(); return new(accepted, conflicts);
    }
    private static string? ArtifactVersion(WorkspaceSource source, string path) => source.Artifacts.SingleOrDefault(a => a.RelativePath == path)?.Version;
    private static Unit FromSource(WorkspaceSourceUnit u) => new(u.EntryId.Value, u.Language, string.Join('.', u.Namespace.Segments.Concat(u.KeyPath.Segments)),
        u.Namespace, u.KeyPath, u.Value, u.Metadata?.Comment ?? string.Empty, u.Metadata?.Approved ?? false, u.Metadata?.Modified, u.Metadata?.ApprovedAt,
        u.Metadata?.ChangeType ?? ChangeType.DirectEdit, u.RawScalar is null ? null : ImmutableDictionary<string, string>.Empty.Add(NestedJsonParser.JsonScalarKey, u.RawScalar), u.Binding);
}
