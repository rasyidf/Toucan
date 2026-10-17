using System.Collections.Immutable;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services;

public sealed partial class WorkspaceService
{
    public WorkspaceRecoveryDraft CaptureRecovery(WorkspaceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (_settings is null || _settings.ProjectPath != source.Root) throw new InvalidOperationException("Recovery source is not the active workspace.");
            WorkspaceDocumentUnit Convert(Unit unit) => new(new(unit.Id), unit.Language, unit.Namespace, unit.KeyPath, unit.Value, unit.Binding,
                unit.FormatData?.GetValueOrDefault(NestedJsonParser.JsonScalarKey), Metadata(unit));
            return new(source.Root, source.FrameworkId, source.FormatId, _baseline.Values.Select(Convert).ToArray(), _state.Values.Select(Convert).ToArray(),
                new(source.Root, source.IdentityCatalog));
        }
    }

    public WorkspaceRecoveryResult ApplyRecovery(WorkspaceRecoveryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var conflicts = new HashSet<EntryId>();
        var applied = 0;
        lock (_gate)
        {
            if (_closing || _disposed || _settings is null || draft.Root != _settings.ProjectPath || draft.IdentityCheckpoint.Root != draft.Root
                || _source is null || _source.FrameworkId != draft.FrameworkId || !FormatIds.Comparer.Equals(_source.FormatId, draft.FormatId))
                throw new InvalidOperationException("Recovery draft belongs to another or closed workspace.");
            var baseline = draft.Baseline.ToDictionary(u => (u.EntryId.Value, u.Language));
            var desired = draft.Units.ToDictionary(u => (u.EntryId.Value, u.Language));
            var next = _state.ToBuilder();
            foreach (var key in baseline.Keys.Union(desired.Keys))
            {
                baseline.TryGetValue(key, out var before); desired.TryGetValue(key, out var after); _state.TryGetValue(key, out var current);
                if (before == after || Matches(current, after)) continue;
                if (!Matches(current, before)) { conflicts.Add(new(key.Item1)); continue; }
                if (after is null) next.Remove(key);
                else
                {
                    if (after.EntryId.Value == Guid.Empty || after.KeyPath.Segments.Count == 0 || after.Metadata is { } metadata &&
                        (metadata.EntryId != after.EntryId || metadata.Language != after.Language)) throw new InvalidOperationException("Invalid recovery entry.");
                    var data = after.RawScalar is null ? null : ImmutableDictionary<string, string>.Empty.Add(NestedJsonParser.JsonScalarKey, after.RawScalar);
                    next[key] = new(key.Item1, after.Language, string.Join('.', after.Namespace.Segments.Concat(after.KeyPath.Segments)),
                        after.Namespace, after.KeyPath, after.Value, after.Metadata?.Comment ?? string.Empty, after.Metadata?.Approved ?? false,
                        after.Metadata?.Modified, after.Metadata?.ApprovedAt, after.Metadata?.ChangeType ?? Contracts.Services.ChangeType.DirectEdit, data, after.Binding);
                }
                applied++;
            }
            var final = next.ToImmutable();
            foreach (var pair in final.Where(p => p.Value.Approved && (!_state.TryGetValue(p.Key, out var old) || !old.Approved)))
                if (ApprovalIssue(final, pair.Value) is not null)
                {
                    if (_state.TryGetValue(pair.Key, out var old)) next[pair.Key] = old; else next.Remove(pair.Key);
                    conflicts.Add(new(pair.Key.Item1)); applied--;
                }
            if (Commit(next.ToImmutable(), WorkspaceChangeOrigin.Lifecycle)) PublishProjections();
        }
        if (applied > 0) RaiseChanged();
        return new(applied, conflicts.ToArray());
    }

    private static bool Matches(Unit? unit, WorkspaceDocumentUnit? draft) => unit is null ? draft is null : draft is not null
        && unit.Id == draft.EntryId.Value && unit.Language == draft.Language && unit.Namespace.Equals(draft.Namespace) && unit.KeyPath.Equals(draft.KeyPath)
        && unit.Value == draft.Value && unit.Binding == draft.Binding && Metadata(unit) == draft.Metadata
        && unit.FormatData?.GetValueOrDefault(NestedJsonParser.JsonScalarKey) == draft.RawScalar;
}
