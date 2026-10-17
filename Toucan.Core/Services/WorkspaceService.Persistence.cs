using System.Collections.Immutable;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services;

public sealed partial class WorkspaceService
{
    private sealed record DocumentCapture(WorkspaceService Owner, ImmutableDictionary<(Guid, string), Unit> State,
        CancellationToken SessionToken);
    private (Guid Session, long Revision) _lastDocumentSave = (Guid.Empty, -1);

    /// <summary>Captures document content and source bindings under the revision gate without retaining editor projections.</summary>
    public WorkspaceDocumentSnapshot? CaptureDocumentSnapshot()
    {
        lock (_gate)
        {
            if (_settings is null) return null;
            RefreshContext();
            return new(_settings.ProjectPath, _session, _revision, _state.Values.Select(u =>
                new WorkspaceDocumentUnit(new(u.Id), u.Language, u.Namespace, u.KeyPath, u.Value, u.Binding,
                    u.FormatData?.GetValueOrDefault(NestedJsonParser.JsonScalarKey), Metadata(u))))
            { Manifest = _settings.SerializeForPersistence(), Capture = new DocumentCapture(this, _state, _sessionLifetime?.Token ?? CancellationToken.None) };
        }
    }

    internal CancellationToken ValidateDocumentSave(WorkspaceDocumentSavePlan plan)
    {
        lock (_gate)
        {
            if (_disposed || _closing || _settings is null || _session != plan.SessionId || _settings.ProjectPath != plan.Root)
                throw new InvalidOperationException("The workspace session is no longer editable.");
            if (_source is not null && _settings.Framework is not null && (_settings.Framework != _source.FrameworkId || !FormatIds.Comparer.Equals(_settings.SaveFormat, _source.FormatId)))
                throw new InvalidOperationException("Changing source capabilities requires reopening the workspace.");
            if (_source is not null && !ReferenceEquals(_source, plan.Source)) throw new InvalidOperationException("Save source is stale; use the workspace current source snapshot.");
            if (plan.Snapshot.Capture is not DocumentCapture capture || !ReferenceEquals(capture.Owner, this))
                throw new InvalidOperationException("Save acknowledgment requires a snapshot captured by this workspace.");
            if (_lastDocumentSave.Session == _session && plan.CapturedRevision <= _lastDocumentSave.Revision)
                throw new InvalidOperationException("A newer document save has already been acknowledged.");
            return capture.SessionToken;
        }
    }

    internal CancellationToken ExportSessionToken(WorkspaceDocumentSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_closing || _disposed || _session != snapshot.SessionId || snapshot.Capture is not DocumentCapture capture || !ReferenceEquals(capture.Owner, this))
                throw new InvalidOperationException("Export belongs to an expired workspace session.");
            return capture.SessionToken;
        }
    }

    /// <summary>Acknowledges exactly the executed capture. Later edits remain dirty; old-session receipts are ignored.</summary>
    public bool AcknowledgeDocumentSave(WorkspaceDocumentSaveReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            var plan = receipt.Plan;
            if (_disposed || _closing || _settings is null || _session != plan.SessionId || _settings.ProjectPath != plan.Root
                || plan.Snapshot.Capture is not DocumentCapture capture || !ReferenceEquals(capture.Owner, this)
                || _lastDocumentSave.Session == _session && plan.CapturedRevision <= _lastDocumentSave.Revision) return false;
            // Standalone execution must meet the same metadata safeguards as the coordinator.
            ValidateDocumentSave(plan);
            var saved = receipt.Source.Units.ToDictionary(u => (u.EntryId.Value, u.Language));
            var known = plan.Source.Units.Select(u => (u.EntryId.Value, u.Language)).Concat(capture.State.Keys).ToHashSet();
            ImmutableDictionary<(Guid, string), Unit> Rebase(ImmutableDictionary<(Guid, string), Unit> state, bool live)
            {
                var result = state.ToBuilder();
                foreach (var (key, unit) in state)
                {
                    if (!known.Contains(key)) continue;
                    saved.TryGetValue(key, out var sourceUnit);
                    var data = unit.FormatData;
                    if (sourceUnit is not null && (live || unit.Value == sourceUnit.Value))
                    {
                        data = sourceUnit.RawScalar is { } raw
                            ? (data ?? ImmutableDictionary<string, string>.Empty).SetItem(NestedJsonParser.JsonScalarKey, raw)
                            : data?.Remove(NestedJsonParser.JsonScalarKey);
                        if (data?.Count == 0) data = null;
                    }
                    var rebased = unit with { Binding = sourceUnit?.Binding, FormatData = data };
                    if (!Equivalent(unit, rebased)) result[key] = rebased;
                }
                return result.ToImmutable();
            }
            _source = receipt.Source;
            _baseline = Rebase(capture.State, live: true);
            _state = Rebase(_state, live: true);
            for (var node = _undo.First; node is not null; node = node.Next)
                node.Value = (Rebase(node.Value.Before, live: false), Rebase(node.Value.After, live: false));
            var redo = _redo.ToArray();
            _redo.Clear();
            foreach (var item in redo.Reverse()) _redo.Push((Rebase(item.Before, live: false), Rebase(item.After, live: false)));
            _lastDocumentSave = (_session, plan.CapturedRevision);
            _revision++;
            PublishProjections();
            PublishChangeLocked(WorkspaceChangeKind.Saved, WorkspaceChangeOrigin.Persistence);
        }
        RaiseChanged();
        return true;
    }

    private static WorkspaceEntryMetadata? Metadata(Unit unit)
    {
        var metadata = new WorkspaceEntryMetadata(new(unit.Id), unit.Language, unit.Comment, unit.Approved, unit.Modified, unit.ApprovedAt, unit.ChangeType);
        return metadata.IsEmpty ? null : metadata;
    }
}
