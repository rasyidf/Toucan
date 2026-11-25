using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

public sealed record SyncProgress(int Done, int Total, string Message);

/// <summary>What a push (or a restart recovery) did, per operation. Nothing is rolled back at the service.</summary>
public sealed record PushResult(int Confirmed, int Failed, int Unknown, int Pending, IReadOnlyList<SyncConflict> Conflicts, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Failed == 0 && Unknown == 0 && Pending == 0 && Conflicts.Count == 0;
    public static PushResult Empty { get; } = new(0, 0, 0, 0, [], []);
}

/// <summary>
/// Sends a plan's push side to a unit session (design 7.6). Every operation is journaled before it is sent and again after its
/// result, so a crash leaves a record of what may have reached the service. An operation whose fate is unclear is read back
/// before it is ever sent again, and it keeps its operation ID so a service that deduplicates sees one operation.
/// </summary>
public sealed class SyncExecutor(TimeProvider? time = null)
{
    private const int DefaultBatch = 50;
    private const int MaxSendAttempts = 3;
    private const int MaxJobPolls = 40;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Recovers anything left from an earlier run, then sends the plan's included push changes.</summary>
    public async Task<PushResult> PushAsync(SyncPlan plan, SourceConnectionSettings connection, IUnitSourceSession session, SourceCapabilities capabilities,
        SourceLimits? limits, SyncStateStore store, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var run = new Run(this, connection, session, capabilities, limits, store, progress);
        await run.RecoverAsync(cancellationToken).ConfigureAwait(false);
        run.Enqueue(plan);
        await run.SendPlannedAsync(cancellationToken).ConfigureAwait(false);
        return run.Finish();
    }

    /// <summary>Restart path: reconcile and resend what an earlier run left in the journal. Sends nothing new.</summary>
    public async Task<PushResult> ResumeAsync(SourceConnectionSettings connection, IUnitSourceSession session, SourceCapabilities capabilities,
        SourceLimits? limits, SyncStateStore store, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var run = new Run(this, connection, session, capabilities, limits, store, progress);
        await run.RecoverAsync(cancellationToken).ConfigureAwait(false);
        await run.SendPlannedAsync(cancellationToken).ConfigureAwait(false);
        return run.Finish();
    }

    private sealed class Run
    {
        private readonly SyncExecutor _owner;
        private readonly SourceConnectionSettings _connection;
        private readonly IUnitSourceSession _session;
        private readonly SourceCapabilities _caps;
        private readonly SourceLimits? _limits;
        private readonly SyncStateStore _store;
        private readonly IProgress<SyncProgress>? _progress;
        private readonly SyncJournal _journal;
        private readonly SyncBaseline _baseline;
        private readonly Dictionary<(Guid EntryId, string Language), BaselineUnit> _index;
        private readonly Lock _gate = new();
        private readonly List<SyncConflict> _conflicts = [];
        private readonly List<string> _errors = [];
        private int _confirmed;
        private int _done;
        private int _total;

        public Run(SyncExecutor owner, SourceConnectionSettings connection, IUnitSourceSession session, SourceCapabilities caps, SourceLimits? limits,
            SyncStateStore store, IProgress<SyncProgress>? progress)
        {
            (_owner, _connection, _session, _caps, _limits, _store, _progress) = (owner, connection, session, caps, limits, store, progress);
            _journal = store.LoadJournal();
            _baseline = store.LoadBaseline() ?? new SyncBaseline();
            _index = _baseline.Units.GroupBy(u => (u.EntryId, u.Language)).ToDictionary(g => g.Key, g => g.First());
        }

        public void Enqueue(SyncPlan plan)
        {
            var now = _owner._time.GetUtcNow();
            var languages = _connection.Languages;
            var creates = new Dictionary<(string, string), JournalEntry>();
            foreach (var change in plan.Push.Where(c => c.Include))
            {
                var remoteLanguage = languages.GetValueOrDefault(change.Language, change.Language);
                if (change.Kind == PushKind.Create)
                {
                    var cell = (change.Remote.ResourceId, change.Remote.Name);
                    if (!creates.TryGetValue(cell, out var create))
                    {
                        creates[cell] = create = New(change, JournalKind.CreateKey, remoteLanguage, now);
                        create.Values = new(StringComparer.Ordinal);
                        _journal.Entries.Add(create);
                    }
                    foreach (var (language, value) in change.Values ?? new Dictionary<string, string> { [remoteLanguage] = change.Value ?? string.Empty })
                        create.Values![language] = value;
                    create.Members.Add(new() { EntryId = change.EntryId, Language = change.Language });
                    continue;
                }
                var entry = New(change, change.Kind switch
                {
                    PushKind.Value => JournalKind.PutValue,
                    PushKind.Status => JournalKind.PutStatus,
                    PushKind.Comment => JournalKind.PutComment,
                    PushKind.Rename => JournalKind.RenameKey,
                    PushKind.DeleteKey => JournalKind.DeleteKey,
                    _ => JournalKind.DeleteTranslation,
                }, remoteLanguage, now);
                entry.Payload = change.Kind == PushKind.Rename ? change.NewName : change.Kind == PushKind.Comment ? change.Comment : change.Value;
                entry.Status = change.Status;
                entry.Members.Add(new() { EntryId = change.EntryId, Language = change.Language });
                _journal.Entries.Add(entry);
            }
            _store.SaveJournal(_journal);
        }

        private static JournalEntry New(PushChange change, JournalKind kind, string remoteLanguage, DateTimeOffset now) => new()
        {
            OperationId = Guid.NewGuid(), Kind = kind, EntryId = change.EntryId, Language = change.Language, RemoteLanguage = remoteLanguage,
            Target = change.Remote, ExpectedRevision = change.ExpectedRevision, BaseValue = change.BaseValue, State = JournalState.Planned, CreatedAt = now,
        };

        // ───────── recovery ─────────

        public async Task RecoverAsync(CancellationToken ct)
        {
            if (_session is ISupportsAsyncJobs jobs)
                foreach (var entry in _journal.Entries.Where(e => e.JobId is not null && e.State is JournalState.InFlight or JournalState.Unknown).ToList())
                    await PollJobAsync(jobs, [entry], entry.JobId!, ct).ConfigureAwait(false);

            var unclear = _journal.Entries.Where(e => e.State is JournalState.InFlight or JournalState.Unknown).ToList();
            if (unclear.Count == 0) return;

            // Read the affected resources completely before judging anything.
            var resources = unclear.Select(e => e.Target.ResourceId).Distinct().ToList();
            var remote = new List<RemoteUnit>();
            var complete = false;
            try
            {
                await foreach (var page in _session.ReadUnitsAsync(new(resources), ct).ConfigureAwait(false))
                {
                    remote.AddRange(page.Units);
                    complete = page.IsComplete;
                }
            }
            catch (SourceException ex)
            {
                _errors.Add(ex.Error.Message);
                return; // stay Unknown; nothing new is sent for these units
            }
            if (!complete)
            {
                _errors.Add("The service did not finish listing its content, so unclear operations were left for the next run.");
                return;
            }
            foreach (var entry in unclear) Reconcile(entry, remote);
            Save();
        }

        private void Reconcile(JournalEntry entry, List<RemoteUnit> remote)
        {
            RemoteUnit? Find(string language) => remote.FirstOrDefault(r =>
                (entry.Target.RemoteId is { } id ? r.Key.RemoteId == id : r.Key.ResourceId == entry.Target.ResourceId && r.Key.Name == entry.Target.Name) && r.Language == language);
            var unit = Find(entry.RemoteLanguage);
            switch (entry.Kind)
            {
                case JournalKind.PutValue:
                    if (unit is not null && unit.Value == entry.Payload) Confirm(entry, unit.Revision);
                    else if (unit is not null && unit.Value == entry.BaseValue) Resend(entry);
                    else Conflict(entry, unit);
                    break;
                case JournalKind.PutStatus:
                    if (unit?.Status?.Meaning == entry.Status) Confirm(entry, unit?.Revision);
                    else Resend(entry);
                    break;
                case JournalKind.PutComment:
                    if (unit?.Comment == entry.Payload) Confirm(entry, unit?.Revision);
                    else Resend(entry);
                    break;
                case JournalKind.CreateKey:
                    if (remote.Any(r => r.Key.ResourceId == entry.Target.ResourceId && r.Key.Name == entry.Target.Name)) Confirm(entry, unit?.Revision, remote.FirstOrDefault(r => r.Key.Name == entry.Target.Name)?.Key.RemoteId);
                    else Resend(entry);
                    break;
                case JournalKind.RenameKey:
                    if (remote.Any(r => r.Key.ResourceId == entry.Target.ResourceId && r.Key.Name == entry.Payload)) Confirm(entry, unit?.Revision);
                    else if (unit is not null) Resend(entry);
                    else Conflict(entry, null);
                    break;
                case JournalKind.DeleteKey:
                case JournalKind.DeleteTranslation:
                    if (unit is null) Confirm(entry, null);
                    else if (unit.Value == entry.BaseValue) Resend(entry);
                    else Conflict(entry, unit);
                    break;
            }
        }

        private void Resend(JournalEntry entry) { entry.State = JournalState.Planned; entry.JobId = null; }

        // ───────── sending ─────────

        public async Task SendPlannedAsync(CancellationToken ct)
        {
            var planned = _journal.Entries.Where(e => e.State == JournalState.Planned).ToList();
            _total = planned.Count;
            if (planned.Count == 0) return;
            var batchSize = Math.Max(1, _limits?.MaxBatchSize ?? DefaultBatch);
            var concurrency = Math.Max(1, _limits?.MaxConcurrency ?? 1);
            var batches = planned.Chunk(batchSize).ToList();
            using var gate = new SemaphoreSlim(concurrency);
            using var halt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tasks = batches.Select(async batch =>
            {
                await gate.WaitAsync(halt.Token).ConfigureAwait(false);
                try
                {
                    halt.Token.ThrowIfCancellationRequested();
                    await SendBatchAsync(batch, halt.Token).ConfigureAwait(false);
                }
                catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Authentication or SourceErrorKind.Authorization)
                {
                    // Nothing more can be sent with this credential; stop the other batches too.
                    await halt.CancelAsync().ConfigureAwait(false);
                    lock (_gate) _errors.Add(ex.Error.Message);
                }
                finally { gate.Release(); }
            }).ToList();
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }

        private async Task SendBatchAsync(JournalEntry[] batch, CancellationToken ct)
        {
            var pending = batch.ToList();
            for (var attempt = 1; pending.Count > 0 && attempt <= MaxSendAttempts; attempt++)
            {
                lock (_gate)
                {
                    foreach (var e in pending) { e.State = JournalState.InFlight; e.Attempts++; e.LastError = null; }
                    SaveJournalOnly();
                }
                UnitWriteResult? result = null;
                TimeSpan? backoff = null;
                try
                {
                    result = await _session.WriteUnitsAsync(new(pending.Select(ToOperation).ToList()), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // entries stay InFlight and are reconciled by the next run
                }
                catch (SourceException ex) when (ex.Error.Kind == SourceErrorKind.RateLimited)
                {
                    backoff = ex.Error.RetryAfter ?? TimeSpan.FromSeconds(attempt);
                    lock (_gate) { foreach (var e in pending) { e.State = JournalState.Planned; e.LastError = ex.Error.Message; } Save(); }
                }
                catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Authentication or SourceErrorKind.Authorization)
                {
                    lock (_gate) { foreach (var e in pending) { e.State = JournalState.Failed; e.LastError = ex.Error.Message; } Save(); }
                    throw;
                }
                catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Validation or SourceErrorKind.Unsupported or SourceErrorKind.NotFound)
                {
                    lock (_gate) { foreach (var e in pending) { e.State = JournalState.Failed; e.LastError = ex.Error.Message; } Save(); }
                    return;
                }
                catch (Exception ex) when (ex is SourceException or HttpRequestException or TimeoutException or IOException)
                {
                    // The request may have reached the service. Only reading it back can tell.
                    lock (_gate) { foreach (var e in pending) { e.State = JournalState.Unknown; e.LastError = ex.Message; } Save(); }
                    return;
                }
                if (backoff is { } wait)
                {
                    await Task.Delay(wait, _owner._time, ct).ConfigureAwait(false);
                    continue;
                }
                pending = await HandleResultsAsync(pending, result!, ct).ConfigureAwait(false);
                if (pending.Count > 0 && attempt < MaxSendAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), _owner._time, ct).ConfigureAwait(false);
            }
            lock (_gate)
            {
                foreach (var e in pending.Where(e => e.State == JournalState.Planned)) { e.State = JournalState.Failed; e.LastError ??= "The service kept asking to slow down."; }
                Save();
            }
        }

        /// <returns>Entries to send again (rate limited).</returns>
        private async Task<List<JournalEntry>> HandleResultsAsync(List<JournalEntry> sent, UnitWriteResult result, CancellationToken ct)
        {
            var byId = result.Results.ToDictionary(r => r.OperationId);
            var again = new List<JournalEntry>();
            var jobs = new List<JournalEntry>();
            lock (_gate)
            {
                foreach (var entry in sent)
                {
                    if (!byId.TryGetValue(entry.OperationId, out var r)) { entry.State = JournalState.Unknown; entry.LastError = "The service returned no result."; continue; }
                    switch (r.Outcome)
                    {
                        case UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied:
                            Confirm(entry, r.NewRevision, r.AssignedRemoteId);
                            break;
                        case UnitOperationOutcome.Conflict:
                            Conflict(entry, r.CurrentRemote);
                            break;
                        case UnitOperationOutcome.RateLimited:
                            entry.State = JournalState.Planned;
                            entry.LastError = r.Error?.Message;
                            again.Add(entry);
                            break;
                        case UnitOperationOutcome.Pending:
                            entry.JobId = result.Job?.Id;
                            entry.State = JournalState.InFlight;
                            jobs.Add(entry);
                            break;
                        case UnitOperationOutcome.Unknown or UnitOperationOutcome.None:
                            entry.State = JournalState.Unknown;
                            entry.LastError = r.Error?.Message;
                            break;
                        default:
                            entry.State = JournalState.Failed;
                            entry.LastError = r.Error?.Message ?? r.Outcome.ToString();
                            _errors.Add($"{entry.Target.Name} ({entry.Language}): {entry.LastError}");
                            break;
                    }
                }
                Save();
            }
            if (jobs.Count > 0 && result.Job is { } job && _session is ISupportsAsyncJobs asyncJobs)
                await PollJobAsync(asyncJobs, jobs, job.Id, ct, job.PollAfter).ConfigureAwait(false);
            return again;
        }

        private async Task PollJobAsync(ISupportsAsyncJobs jobs, IReadOnlyList<JournalEntry> entries, string jobId, CancellationToken ct, TimeSpan? first = null)
        {
            var delay = first ?? TimeSpan.FromSeconds(1);
            for (var poll = 0; poll < MaxJobPolls; poll++)
            {
                await Task.Delay(delay, _owner._time, ct).ConfigureAwait(false);
                SourceJobStatus status;
                try { status = await jobs.GetJobAsync(new(jobId), ct).ConfigureAwait(false); }
                catch (SourceException ex) when (ex.Error.IsTransient) { delay = ex.Error.RetryAfter ?? TimeSpan.FromSeconds(2); continue; }
                if (status.State == SourceJobState.Running) { delay = TimeSpan.FromSeconds(Math.Min(10, 1 + poll)); continue; }
                lock (_gate)
                {
                    var byId = status.Results?.ToDictionary(r => r.OperationId) ?? [];
                    foreach (var entry in entries)
                    {
                        if (status.State == SourceJobState.Failed || !byId.TryGetValue(entry.OperationId, out var r))
                        {
                            entry.State = status.State == SourceJobState.Failed ? JournalState.Failed : JournalState.Unknown;
                            entry.LastError = status.Error?.Message ?? "The job ended without a result.";
                        }
                        else if (r.Outcome is UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied) Confirm(entry, r.NewRevision, r.AssignedRemoteId);
                        else if (r.Outcome == UnitOperationOutcome.Conflict) Conflict(entry, r.CurrentRemote);
                        else { entry.State = JournalState.Failed; entry.LastError = r.Error?.Message ?? r.Outcome.ToString(); }
                        entry.JobId = null;
                    }
                    Save();
                }
                return;
            }
            // Still running: keep the handle so the next run resumes polling.
            lock (_gate) { foreach (var entry in entries) entry.JobId = jobId; Save(); }
        }

        private UnitWriteOperation ToOperation(JournalEntry e)
        {
            var key = e.Target.ToRemote();
            return e.Kind switch
            {
                JournalKind.PutValue => new PutValue(e.OperationId, key, e.RemoteLanguage, e.Payload ?? string.Empty, e.ExpectedRevision),
                JournalKind.PutStatus => new PutStatus(e.OperationId, key, e.RemoteLanguage, e.Status ?? SourceReviewMeaning.Draft, e.ExpectedRevision),
                JournalKind.PutComment => new PutComment(e.OperationId, key, e.RemoteLanguage, e.Payload ?? string.Empty),
                JournalKind.CreateKey => new CreateRemoteKey(e.OperationId, key, e.Values ?? new Dictionary<string, string>()),
                JournalKind.RenameKey => new RenameRemoteKey(e.OperationId, key, e.Payload ?? string.Empty),
                JournalKind.DeleteKey => new DeleteRemoteKey(e.OperationId, key),
                _ => new DeleteTranslation(e.OperationId, key, e.RemoteLanguage, e.ExpectedRevision),
            };
        }

        // ───────── outcomes ─────────

        private void Confirm(JournalEntry entry, string? revision, string? assignedRemoteId = null)
        {
            entry.State = JournalState.Confirmed;
            entry.ResultRevision = revision;
            entry.AssignedRemoteId = assignedRemoteId;
            var now = _owner._time.GetUtcNow();
            var members = entry.Members.Count > 0 ? entry.Members : [new() { EntryId = entry.EntryId, Language = entry.Language }];
            var byUnit = _index;
            switch (entry.Kind)
            {
                case JournalKind.PutValue:
                case JournalKind.PutStatus:
                case JournalKind.PutComment:
                    foreach (var m in members)
                    {
                        var unit = byUnit.GetValueOrDefault((m.EntryId, m.Language)) ?? new BaselineUnit { EntryId = m.EntryId, Language = m.Language, Remote = entry.Target };
                        if (entry.Kind == JournalKind.PutValue) { unit.Value = entry.Payload ?? string.Empty; unit.ValueHash = BaselineUnit.Hash(unit.Value); }
                        if (entry.Kind == JournalKind.PutStatus) unit.Status = entry.Status;
                        if (entry.Kind == JournalKind.PutComment) unit.CommentHash = BaselineUnit.Hash(entry.Payload ?? string.Empty);
                        unit.RemoteRevision = revision ?? unit.RemoteRevision;
                        unit.SyncedAt = now;
                        byUnit[(m.EntryId, m.Language)] = unit;
                    }
                    break;
                case JournalKind.CreateKey:
                    foreach (var m in members)
                    {
                        var remoteLanguage = _connection.Languages.GetValueOrDefault(m.Language, m.Language);
                        var value = entry.Values?.GetValueOrDefault(remoteLanguage) ?? string.Empty;
                        byUnit[(m.EntryId, m.Language)] = new BaselineUnit
                        {
                            EntryId = m.EntryId, Language = m.Language, Remote = entry.Target with { RemoteId = assignedRemoteId ?? entry.Target.RemoteId },
                            RemoteRevision = revision, Value = value, ValueHash = BaselineUnit.Hash(value), SyncedAt = now,
                        };
                    }
                    break;
                case JournalKind.RenameKey:
                    foreach (var unit in byUnit.Values.Where(u => SameKey(u.Remote, entry.Target)))
                        unit.Remote = unit.Remote with { Name = entry.Payload ?? unit.Remote.Name };
                    break;
                case JournalKind.DeleteKey:
                    foreach (var key in byUnit.Where(p => SameKey(p.Value.Remote, entry.Target)).Select(p => p.Key).ToList()) byUnit.Remove(key);
                    break;
                case JournalKind.DeleteTranslation:
                    foreach (var m in members) byUnit.Remove((m.EntryId, m.Language));
                    break;
            }
            _baseline.LastPush = now;
            _baselineDirty = true;
            Interlocked.Increment(ref _confirmed);
            Interlocked.Increment(ref _done);
            _progress?.Report(new(_done, _total, $"Sent {entry.Target.Name}"));
        }

        private static bool SameKey(SyncRemoteKey a, SyncRemoteKey b) =>
            a.RemoteId is not null && a.RemoteId == b.RemoteId || a.ResourceId == b.ResourceId && a.Name == b.Name;

        private void Conflict(JournalEntry entry, RemoteUnit? current)
        {
            entry.State = JournalState.Conflict;
            _conflicts.Add(new()
            {
                Kind = SyncConflictKind.BothChanged, Field = entry.Kind == JournalKind.PutStatus ? SyncField.Status : entry.Kind == JournalKind.PutComment ? SyncField.Comment : SyncField.Value,
                EntryId = entry.EntryId, Language = entry.Language, Remote = entry.Target, Base = entry.BaseValue, Local = entry.Payload,
                RemoteValue = current?.Value, RemoteRevision = current?.Revision, RemoteStatus = current?.Status?.Meaning,
            });
            Interlocked.Increment(ref _done);
        }

        private bool _baselineDirty;

        private void Save()
        {
            // Confirmed entries are done: the baseline holds their result, so the journal drops them. The baseline is large
            // (tens of megabytes at 100,000 translations), so it is written only when a result changed it.
            if (_baselineDirty) { _baseline.Units = [.. _index.Values]; _store.SaveBaseline(_baseline); _baselineDirty = false; }
            SaveJournalOnly();
        }

        private void SaveJournalOnly() =>
            _store.SaveJournal(new SyncJournal { Entries = [.. _journal.Entries.Where(e => e.State != JournalState.Confirmed)] });

        public PushResult Finish()
        {
            lock (_gate)
            {
                var active = _journal.Entries.ToList();
                _journal.Entries.RemoveAll(e => e.State == JournalState.Confirmed);
                Save();
                return new(_confirmed, active.Count(e => e.State == JournalState.Failed), active.Count(e => e.State is JournalState.Unknown),
                    active.Count(e => e.State == JournalState.InFlight), _conflicts, _errors);
            }
        }
    }
}
