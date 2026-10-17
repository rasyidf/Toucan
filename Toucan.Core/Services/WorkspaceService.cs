using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Core.Services;

/// <summary>
/// Owns committed workspace state, revisions, baselines, and transaction history. TranslationItem instances
/// are compatibility projections for existing editors/persistence; none are retained as canonical state.
/// </summary>
public sealed partial class WorkspaceService(ITranslationManagementService store, IValidationPipeline validation, ILogger<WorkspaceService>? logger = null, int commandCapacity = 128) : IWorkspaceSubscriptionBackend, IDisposable
{
    private readonly object _gate = new();
    private ProjectSettings? _settings;
    private WorkspaceSource? _source;
    public WorkspaceSource? Source { get { lock (_gate) return _source; } }
    private Guid _session;
    private bool _closing;
    private long _revision;
    private string _contextStamp = string.Empty;
    private bool _deferredNotification;
    private ImmutableDictionary<(Guid Id, string Language), Unit> _state = ImmutableDictionary<(Guid, string), Unit>.Empty;
    private ImmutableDictionary<(Guid Id, string Language), Unit> _baseline = ImmutableDictionary<(Guid, string), Unit>.Empty;
    private readonly ConditionalWeakTable<TranslationItem, Identity> _identities = new();
    private sealed record Identity(Guid Id);
    private readonly Dictionary<(Guid, string), TranslationItem> _projections = [];
    private readonly LinkedList<(ImmutableDictionary<(Guid, string), Unit> Before, ImmutableDictionary<(Guid, string), Unit> After)> _undo = [];
    private readonly Stack<(ImmutableDictionary<(Guid, string), Unit> Before, ImmutableDictionary<(Guid, string), Unit> After)> _redo = [];

    private sealed record Unit(Guid Id, string Language, string Key, TranslationPath Namespace, TranslationPath KeyPath,
        string Value, string Comment, bool Approved, DateTime? Modified, DateTime? ApprovedAt, ChangeType ChangeType,
        ImmutableDictionary<string, string>? FormatData, SourceBinding? Binding = null);

    public event EventHandler? Changed;
    public bool IsOpen { get { lock (_gate) return _settings is not null; } }
    public bool IsDirty { get { lock (_gate) return !Same(_state, _baseline); } }
    public long Revision { get { lock (_gate) return _revision; } }
    public Guid SessionId { get { lock (_gate) return _session; } }
    public string? ProjectPath { get { lock (_gate) return _settings?.ProjectPath; } }
    internal bool AcceptsEdits { get { lock (_gate) return _settings is not null && !_closing; } }

    public void Open(ProjectSettings settings, WorkspaceSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EndSessionLocked();
            _sessionLifetime = new CancellationTokenSource();
            _settings = settings;
            _source = source;
            _session = Guid.NewGuid();
            _closing = false;
            _identities.Clear();
            _projections.Clear();
            _undo.Clear();
            _redo.Clear();
            _state = ImmutableDictionary<(Guid, string), Unit>.Empty;
            if (source is null) _state = Capture(store.Translations);
            else
            {
                _state = source.Units.ToImmutableDictionary(u => (u.EntryId.Value, u.Language), u => new Unit(u.EntryId.Value,
                    u.Language, string.Join('.', u.Namespace.Segments.Concat(u.KeyPath.Segments)), u.Namespace, u.KeyPath,
                    u.Value, u.Metadata?.Comment ?? string.Empty, u.Metadata?.Approved ?? false, u.Metadata?.Modified, u.Metadata?.ApprovedAt, u.Metadata?.ChangeType ?? ChangeType.DirectEdit,
                    u.RawScalar is null ? null : ImmutableDictionary<string, string>.Empty.Add(NestedJsonParser.JsonScalarKey, u.RawScalar), u.Binding));
                PublishProjections();
                store.Initialize(store.Translations.ToList());
            }
            _baseline = _state;
            _contextStamp = ContextSignature();
            _revision++;
            ResetPublicationLocked();
            StartCommandSessionLocked();
        }
        RaiseChanged();
    }

    public void Close()
    {
        lock (_gate)
        {
            EndSessionLocked();
            _settings = null;
            _source = null;
            _state = _baseline = ImmutableDictionary<(Guid, string), Unit>.Empty;
            _identities.Clear();
            _projections.Clear();
            _undo.Clear();
            _redo.Clear();
            ResetPublicationLocked();
        }
        RaiseChanged();
    }

    /// <summary>Read access survives plugin teardown; new commands are refused once closing begins.</summary>
    public void BeginClosing()
    {
        lock (_gate)
        {
            if (_settings is null || _closing) return;
            _closing = true;
            StopCommandSessionLocked();
            _sessionLifetime?.Cancel();
            _revision++;
            PublishChangeLocked(WorkspaceChangeKind.Closing, WorkspaceChangeOrigin.Lifecycle);
        }
        RaiseChanged();
    }

    // Legacy persistence is synchronous. Keep its captured state and acknowledgement inside one mutation lease.
    internal IDisposable ProtectPersistence() => new PersistenceLease(this);
    private sealed class PersistenceLease : IDisposable
    {
        private WorkspaceService? _owner;
        public PersistenceLease(WorkspaceService owner) { _owner = owner; Monitor.Enter(owner._gate); }
        public void Dispose()
        {
            if (_owner is not { } owner) return;
            _owner = null;
            var notify = owner._deferredNotification;
            owner._deferredNotification = false;
            Monitor.Exit(owner._gate);
            if (notify) owner.RaiseChanged();
        }
    }

    internal IReadOnlyList<TranslationItem> SaveProjection()
    {
        lock (_gate) return _state.Values.Select(Project).ToList();
    }

    private void RaiseChanged()
    {
        if (Monitor.IsEntered(_gate)) { _deferredNotification = true; return; }
        foreach (var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try { ((EventHandler)subscriber)(this, EventArgs.Empty); }
            catch (Exception ex) { logger?.LogWarning(ex, "Workspace change subscriber failed"); }
        }
    }

    /// <summary>Accepts a native editor transaction. Objects passed here remain projections, not workspace state.</summary>
    public EditResult CommitProjection(IReadOnlyList<TranslationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        bool changed;
        int changedUnits;
        long revision;
        lock (_gate)
        {
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            var next = Capture(items);
            foreach (var unit in next.Values.Where(u => u.Approved && (!_state.TryGetValue((u.Id, u.Language), out var old) || !old.Approved)))
            {
                var problem = ApprovalIssue(next, unit);
                if (problem is null) continue;
                PublishProjections();
                return Refused(problem.Value, "Approval refused by the workspace policy.");
            }
            var contextChanged = RefreshContext();
            changedUnits = next.Count(p => !_state.TryGetValue(p.Key, out var old) || !Equivalent(old, p.Value)) + _state.Keys.Count(k => !next.ContainsKey(k));
            changed = Commit(next) || contextChanged;
            revision = _revision;
        }
        if (changed) RaiseChanged();
        return new(true, changedUnits, revision, [], []);
    }

    /// <summary>Reload/recovery replaces the projection and invalidates prior commands and history.</summary>
    public void ReloadProjection(bool saved)
    {
        lock (_gate)
        {
            if (_settings is null || _closing || _disposed) return;
            _state = Capture(store.Translations);
            if (saved) _baseline = _state;
            else
            {
                foreach (var item in store.Translations)
                {
                    if (!_identities.TryGetValue(item, out var identity) || !store.TryGetSavedState(item, out var value, out var comment)) continue;
                    var key = (identity.Id, item.Language);
                    if (!_state.TryGetValue(key, out var current)) continue;
                    _baseline.TryGetValue(key, out var previous);
                    _baseline = _baseline.SetItem(key, current with { Value = value, Comment = comment,
                        Approved = previous?.Approved ?? current.Approved, ApprovedAt = previous?.ApprovedAt });
                }
            }
            _undo.Clear();
            _redo.Clear();
            _revision++;
            PublishChangeLocked(WorkspaceChangeKind.Reloaded, WorkspaceChangeOrigin.Source);
        }
        RaiseChanged();
    }

    public void MarkSaved()
    {
        lock (_gate)
        {
            if (_settings is null || _closing || _disposed) return;
            _baseline = _state;
            _revision++;
            PublishChangeLocked(WorkspaceChangeKind.Saved, WorkspaceChangeOrigin.Persistence);
        }
        RaiseChanged();
    }

    public void MaterializeMissing(IReadOnlyList<TranslationItem> added)
    {
        lock (_gate)
        {
            if (_settings is null || _closing || _disposed) return;
            _state = Capture(store.Translations);
            foreach (var item in added)
            {
                var key = (_identities.GetValue(item, _ => throw new InvalidOperationException("Projection has no identity.")).Id, item.Language);
                _baseline = _baseline.SetItem(key, _state[key]);
            }
            _revision++;
            PublishChangeLocked(WorkspaceChangeKind.Updated, WorkspaceChangeOrigin.Lifecycle);
        }
        RaiseChanged();
    }

    public IReadOnlyList<ValidationResult> Validate()
    {
        lock (_gate) return _settings is null ? [] : Validation(_state);
    }

    public IReadOnlyList<TranslationItem> ApprovalBlockers(IReadOnlyCollection<TranslationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_gate)
        {
            if (_settings is null || _closing) return items.ToList();
            var errors = _settings.RequireValidForApproval == true
                ? Validation(_state).Where(r => r.Severity == ValidationSeverity.Error).Select(r => (r.Namespace, r.Language)).ToHashSet()
                : [];
            return items.Where(item => !_identities.TryGetValue(item, out var identity)
                || !_state.TryGetValue((identity.Id, item.Language), out var unit) || string.IsNullOrEmpty(unit.Value)
                || errors.Contains((unit.Key, unit.Language)) || errors.Contains((unit.Key, null))).ToList();
        }
    }

    public bool Undo() => Restore(undo: true);
    public bool Redo() => Restore(undo: false);

    private bool Restore(bool undo)
    {
        lock (_gate)
        {
            if (_settings is null || _closing) return false;
            if (undo)
            {
                if (_undo.Last is not { } last) return false;
                _undo.RemoveLast();
                _redo.Push(last.Value);
                _state = last.Value.Before;
            }
            else
            {
                if (!_redo.TryPop(out var step)) return false;
                _undo.AddLast(step);
                _state = step.After;
            }
            _revision++;
            PublishProjections();
            PublishChangeLocked(undo ? WorkspaceChangeKind.Undo : WorkspaceChangeKind.Redo, WorkspaceChangeOrigin.History);
        }
        RaiseChanged();
        return true;
    }

    public Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshContext();
            var snapshot = _settings is null ? null : new WorkspaceSnapshot(_settings.ProjectPath, _settings.ProjectPath,
                PrimaryLanguage(), Languages(), _state.Values.Select(u => new UnitSnapshot(u.Key, u.Language, u.Value,
                    u.Comment, u.Approved ? ReviewState.Approved : ReviewState.Draft, Dirty(u))).ToImmutableArray(), _revision);
            return Task.FromResult(snapshot);
        }
    }

    public Task<IdentifiedWorkspaceSnapshot?> SnapshotEntriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshContext();
            var snapshot = SnapshotEntriesLocked();
            return Task.FromResult(snapshot);
        }
    }

    public EditResult Apply(string pluginId, WorkspaceEdit edit, Guid? expectedSession = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();
        EditResult result;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            if (expectedSession is { } session && session != _session) return Refused(EditIssueKind.Stale, "The workspace session changed.");
            RefreshContext();
            var items = _state.Values.Select(Project).ToList();
            var plan = WorkspaceEditPlanner.Plan(new(items, Languages(), PrimaryLanguage(), _revision,
                _settings.RequireValidForApproval == true, validation,
                _state.Values.GroupBy(u => u.Key).Where(g => g.Select(u => u.Id).Distinct().Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal)), edit);
            if (!plan.CanApply) return new EditResult(false, 0, _revision, plan.Issues, []);
            if (plan.Steps.Count == 0) return new EditResult(true, 0, _revision, plan.Issues, []);
            foreach (var (_, operation) in plan.Steps) ApplyLegacy(items, operation);
            var next = Capture(items, adoptProjection: false);
            var findings = Findings(next);
            cancellationToken.ThrowIfCancellationRequested();
            Commit(next, WorkspaceChangeOrigin.Plugin, pluginId);
            PublishProjections();
            result = new(true, plan.ChangedUnits, _revision, plan.Issues, findings);
        }
        RaiseChanged();
        return result;
    }

    public EditResult ApplyEntries(string pluginId, EntryEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();
        EditResult result;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            RefreshContext();
            if (edit.SessionId != _session || edit.BasedOnRevision != _revision)
                return Refused(EditIssueKind.Stale, "The workspace session or revision changed.");
            var next = _state.ToBuilder();
            foreach (var operation in edit.Operations)
            {
                var matching = next.Values.Where(u => u.Id == operation.EntryId).ToList();
                if (matching.Count == 0) return Refused(EditIssueKind.UnknownKey, "Entry does not exist.");
                switch (operation)
                {
                    case DeleteEntry:
                        foreach (var unit in matching) next.Remove((unit.Id, unit.Language));
                        break;
                    case RenameEntry rename:
                        if (rename.KeyPath.Segments.Count == 0) return Refused(EditIssueKind.InvalidKey, "Key path cannot be empty.");
                        foreach (var unit in matching)
                        {
                            if (next.Values.Any(u => u.Id != unit.Id && u.Language == unit.Language && u.Binding?.DocumentId == unit.Binding?.DocumentId
                                && u.Namespace.Equals(rename.Namespace) && Conflicts(u.KeyPath, rename.KeyPath)))
                                return Refused(EditIssueKind.KeyExists, "Destination key already exists.");
                            next[(unit.Id, unit.Language)] = unit with { Namespace = rename.Namespace, KeyPath = rename.KeyPath,
                                Key = string.Join('.', rename.Namespace.Segments.Concat(rename.KeyPath.Segments)) };
                        }
                        break;
                    default:
                        var language = operation switch { SetEntryValue v => v.Language, SetEntryComment c => c.Language, SetEntryReview r => r.Language, _ => null };
                        if (language is null) return Refused(EditIssueKind.InvalidKey, "Unknown entry operation.");
                        if (!next.TryGetValue((operation.EntryId, language), out var current))
                            return Refused(EditIssueKind.UnknownLanguage, "Entry has no translation in that language.");
                        if (operation is SetEntryValue { ExpectedValue: { } expected } && current.Value != expected)
                            return Refused(EditIssueKind.Conflict, "Translation changed since it was read.");
                        var now = DateTime.UtcNow;
                        next[(current.Id, current.Language)] = operation switch
                        {
                            SetEntryValue value when value.Value != current.Value => current with { Value = value.Value, Modified = now, ChangeType = ChangeType.External },
                            SetEntryComment comment when comment.Comment != current.Comment => current with { Comment = comment.Comment, Modified = now },
                            SetEntryReview review when current.Approved != (review.State == ReviewState.Approved) => current with { Approved = review.State == ReviewState.Approved,
                                ApprovedAt = review.State == ReviewState.Approved ? now : null },
                            _ => current
                        };
                        break;
                }
            }
            var final = next.ToImmutable();
            foreach (var review in edit.Operations.OfType<SetEntryReview>().Where(r => r.State == ReviewState.Approved))
            {
                if (!final.TryGetValue((review.EntryId, review.Language), out var approved) || !approved.Approved) continue;
                if (ApprovalIssue(final, approved) is { } problem)
                    return Refused(problem, "Approval refused by the workspace policy.");
            }
            var changed = final.Count(p => !_state.TryGetValue(p.Key, out var old) || !Equivalent(old, p.Value)) + _state.Keys.Count(k => !final.ContainsKey(k));
            var findings = Findings(final);
            cancellationToken.ThrowIfCancellationRequested();
            Commit(final, WorkspaceChangeOrigin.Plugin, pluginId);
            PublishProjections();
            result = new(true, changed, _revision, [], findings);
        }
        if (result.ChangedUnits > 0) RaiseChanged();
        return result;
    }

    private EditResult Refused(EditIssueKind kind, string message) => EditResult.Refused(_revision, new EditIssue(-1, kind, message));
    private string ContextSignature() => _settings is null ? string.Empty : JsonSerializer.Serialize(new
        { _settings.PrimaryLanguage, _settings.RequireValidForApproval, _settings.Languages, _settings.SaveFormat, _settings.Framework });
    private bool RefreshContext()
    {
        var signature = ContextSignature();
        if (signature == _contextStamp) return false;
        _contextStamp = signature;
        _revision++;
        PublishChangeLocked(WorkspaceChangeKind.ConfigurationChanged, WorkspaceChangeOrigin.Configuration);
        return true;
    }
    private IReadOnlyList<string> Languages()
    {
        var loaded = _state.Values.Select(u => u.Language).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        return loaded.Length > 0 ? loaded : (_settings?.Languages ?? []).ToImmutableArray();
    }
    private string PrimaryLanguage()
    {
        var languages = Languages();
        var configured = _settings!.PrimaryLanguage;
        return languages.FirstOrDefault(l => string.Equals(l, configured, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(l => l.StartsWith(configured.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            ?? languages.Order(StringComparer.Ordinal).FirstOrDefault() ?? configured;
    }
    private bool Dirty(Unit unit) => !_baseline.TryGetValue((unit.Id, unit.Language), out var saved) || !Equivalent(unit, saved);
    private static bool Equivalent(Unit a, Unit b) => a.Id == b.Id && a.Language == b.Language && a.Key == b.Key
        && a.Namespace.Equals(b.Namespace) && a.KeyPath.Equals(b.KeyPath) && a.Value == b.Value && a.Comment == b.Comment && a.Approved == b.Approved && a.Binding == b.Binding
        && a.Modified == b.Modified && a.ApprovedAt == b.ApprovedAt && a.ChangeType == b.ChangeType
        && (a.FormatData is null ? b.FormatData is null : b.FormatData is not null && a.FormatData.Count == b.FormatData.Count
            && a.FormatData.All(p => b.FormatData.TryGetValue(p.Key, out var value) && p.Value == value));
    private static bool Conflicts(TranslationPath a, TranslationPath b) =>
        a.Segments.Take(Math.Min(a.Segments.Count, b.Segments.Count)).SequenceEqual(b.Segments.Take(Math.Min(a.Segments.Count, b.Segments.Count)), StringComparer.Ordinal);
    private static bool Same(ImmutableDictionary<(Guid, string), Unit> a, ImmutableDictionary<(Guid, string), Unit> b) =>
        ReferenceEquals(a, b) || a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var other) && Equivalent(p.Value, other));

    private bool Commit(ImmutableDictionary<(Guid, string), Unit> next, WorkspaceChangeOrigin origin = WorkspaceChangeOrigin.Native, string? originId = null)
    {
        if (Same(_state, next)) return false;
        _undo.AddLast((_state, next));
        while (_undo.Count > 200) _undo.RemoveFirst();
        _redo.Clear();
        _state = next;
        _revision++;
        PublishChangeLocked(WorkspaceChangeKind.Updated, origin, originId);
        return true;
    }

    private ImmutableDictionary<(Guid, string), Unit> Capture(IEnumerable<TranslationItem> items, bool adoptProjection = true)
    {
        var result = _state.ToBuilder();
        var retained = new HashSet<(Guid, string)>();
        var entries = _state.Values.GroupBy(u => u.Key).Where(g => g.Select(u => u.Id).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var item in items)
        {
            Guid id;
            if (!_identities.TryGetValue(item, out var identity))
            {
                if (!entries.TryGetValue(item.Namespace, out id)) entries[item.Namespace] = id = Guid.NewGuid();
                _identities.Add(item, new(id));
            }
            else id = identity.Id;
            _state.TryGetValue((id, item.Language), out var previous);
            var keyPath = previous is not null && previous.Key == item.Namespace ? previous.KeyPath : new TranslationPath(item.Namespace.Split('.'));
            var ns = previous is not null && previous.Key == item.Namespace ? previous.Namespace : TranslationPath.Empty;
            if (previous?.Key != item.Namespace && _source is not null)
            {
                var namespaces = _source.Units.Select(u => u.Namespace).Distinct().Where(n => n.Segments.Count > 0
                    && item.Namespace.StartsWith(string.Join('.', n.Segments) + ".", StringComparison.Ordinal)).OrderByDescending(n => n.Segments.Count).ToList();
                if (namespaces.Count == 1)
                {
                    ns = namespaces[0];
                    keyPath = new(item.Namespace[(string.Join('.', ns.Segments).Length + 1)..].Split('.'));
                }
            }
            var key = (id, item.Language);
            if (!retained.Add(key)) throw new InvalidOperationException("A projection contains the same entry/language more than once.");
            var unit = new Unit(id, item.Language, item.Namespace, ns, keyPath, item.Value ?? string.Empty,
                item.Comment ?? string.Empty, item.IsApproved, item.LastModifiedUtc, item.ApprovedAtUtc, item.ChangeType,
                item.FormatData?.ToImmutableDictionary(StringComparer.Ordinal), previous?.Binding);
            if (previous is null || !Equivalent(previous, unit)) result[key] = unit;
            if (adoptProjection) _projections[(id, item.Language)] = item;
        }
        foreach (var key in _state.Keys.Where(k => !retained.Contains(k))) result.Remove(key);
        return result.ToImmutable();
    }

    private TranslationItem Project(Unit unit)
    {
        var item = new TranslationItem { Language = unit.Language };
        Copy(unit, item);
        _identities.Add(item, new(unit.Id));
        return item;
    }

    private static void Copy(Unit unit, TranslationItem item)
    {
        item.Namespace = unit.Key;
        item.Language = unit.Language;
        item.Value = unit.Value;
        item.Comment = unit.Comment;
        item.IsApproved = unit.Approved;
        item.LastModifiedUtc = unit.Modified;
        item.ApprovedAtUtc = unit.ApprovedAt;
        item.ChangeType = unit.ChangeType;
        item.FormatData = unit.FormatData?.ToDictionary();
    }

    private void PublishProjections()
    {
        var active = new HashSet<TranslationItem>(ReferenceEqualityComparer.Instance);
        foreach (var unit in _state.Values)
        {
            if (!_projections.TryGetValue((unit.Id, unit.Language), out var item)) _projections[(unit.Id, unit.Language)] = item = Project(unit);
            Copy(unit, item);
            active.Add(item);
        }
        store.RemoveItems(i => !active.Contains(i));
        var existing = store.Translations.ToHashSet(ReferenceEqualityComparer.Instance);
        store.AddItems(active.Where(i => !existing.Contains(i)).ToList());
        // Structural undo can reintroduce objects whose legacy baselines were removed on delete.
        store.MarkSaved(_baseline.Values.Select(Project));
        foreach (var key in _projections.Keys.Where(k => !_state.ContainsKey(k)).ToList()) _projections.Remove(key);
    }

    private EditIssueKind? ApprovalIssue(ImmutableDictionary<(Guid, string), Unit> state, Unit unit)
    {
        if (string.IsNullOrEmpty(unit.Value)) return EditIssueKind.Empty;
        return _settings!.RequireValidForApproval == true && Validation(state, unit.Id).Any(r => r.Severity == ValidationSeverity.Error
            && r.Namespace == unit.Key && (r.Language is null || r.Language == unit.Language)) ? EditIssueKind.ApprovalRefused : null;
    }

    private IReadOnlyList<ValidationResult> Validation(ImmutableDictionary<(Guid, string), Unit> state, Guid? target = null)
    {
        var ambiguous = state.Values.GroupBy(u => u.Key).Where(g => g.Select(u => u.Id).Distinct().Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<ValidationResult> Run(IEnumerable<Unit> units) => validation.RunAll(new ValidationContext
            { Items = units.Select(Project).ToList(), PrimaryLanguage = PrimaryLanguage() }).ToList();
        if (ambiguous.Count == 0) return Run(state.Values);
        // Legacy validation rules address dotted keys. Isolate ambiguous entries so no rule merges their values.
        var results = Run(state.Values.Where(u => !ambiguous.Contains(u.Key))).ToList();
        foreach (var group in state.Values.Where(u => ambiguous.Contains(u.Key) && (target is null || u.Id == target)).GroupBy(u => u.Id))
            results.AddRange(Run(group));
        return results;
    }
    private IReadOnlyList<EditFinding> Findings(ImmutableDictionary<(Guid, string), Unit> state) => Validation(state).Take(50)
        .Where(r => r.Namespace is not null).Select(r => new EditFinding(r.Namespace!, r.Language, r.Severity.ToString(), r.Message)).ToImmutableArray();

    private void ApplyLegacy(List<TranslationItem> items, EditOperation operation)
    {
        TranslationItem Find(string key, string language) => items.Single(i => i.Namespace == key && i.Language == language);
        static bool Matches(string key, string parent) => key == parent || key.StartsWith(parent + ".", StringComparison.Ordinal);
        var now = DateTime.UtcNow;
        switch (operation)
        {
            case SetValue value:
                var item = Find(value.Key, value.Language);
                item.Value = value.Value;
                item.ChangeType = ChangeType.External;
                item.LastModifiedUtc = now;
                break;
            case SetComment comment: Find(comment.Key, comment.Language).Comment = comment.Comment; break;
            case SetReview review:
                var approved = Find(review.Key, review.Language);
                approved.IsApproved = review.State == ReviewState.Approved;
                approved.ApprovedAtUtc = approved.IsApproved ? now : null;
                break;
            case AddKey add:
                var id = Guid.NewGuid();
                foreach (var language in Languages())
                {
                    var added = new TranslationItem { Namespace = add.Key, Language = language,
                        Value = add.Values?.FirstOrDefault(v => string.Equals(v.Key, language, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty,
                        LastModifiedUtc = now, ChangeType = ChangeType.External };
                    _identities.Add(added, new(id));
                    items.Add(added);
                }
                break;
            case RenameKey rename:
                foreach (var renamed in items.Where(i => Matches(i.Namespace, rename.Key)))
                    renamed.Namespace = rename.NewKey + renamed.Namespace[rename.Key.Length..];
                break;
            case DeleteKey delete: items.RemoveAll(i => Matches(i.Namespace, delete.Key)); break;
        }
    }
}
