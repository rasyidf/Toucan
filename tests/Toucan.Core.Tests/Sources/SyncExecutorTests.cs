using Toucan.Core.Models;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>Journaled pushes: batching, ambiguous failures, restart recovery, duplicate prevention, jobs, cancellation.</summary>
public sealed class SyncExecutorTests : IAsyncLifetime
{
    private const SourceCapabilities Caps = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.ConditionalWrites | SourceCapabilities.AsyncJobs;
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-sync-").FullName;
    private readonly ScriptedUnitSession _session = new();
    private readonly SyncExecutor _executor = new();
    private readonly SyncStateStore _store;
    private readonly SourceConnectionSettings _connection = new() { Id = "c1", Adapter = "x.y", Languages = { ["fr"] = "fr" } };

    public SyncExecutorTests() => _store = new SyncStateStore(_root, "c1");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        TempFolder.TryDelete(_root);
    }

    private static PushChange Put(Guid entry, string key, string value, string? expected, string? baseValue = "old") => new()
    {
        Kind = PushKind.Value, EntryId = entry, Language = "fr", Remote = new SyncRemoteKey("default", key, "id-" + key), Value = value, ExpectedRevision = expected, BaseValue = baseValue,
    };

    private static SyncPlan PlanOf(params PushChange[] changes) =>
        new("c1", Guid.NewGuid(), SyncPlanKind.Push, 1, DateTimeOffset.UnixEpoch, true, false, [], changes, [], []);

    private Task<PushResult> PushAsync(SyncPlan plan, SourceLimits? limits = null, CancellationToken ct = default) =>
        _executor.PushAsync(plan, _connection, _session, Caps, limits, _store, null, ct);

    private Task<PushResult> ResumeAsync() => _executor.ResumeAsync(_connection, _session, Caps, null, _store);

    private void Bind(Guid entry, string key, string value, string revision) =>
        _store.SaveBaseline(new SyncBaseline
        {
            Units = [.. (_store.LoadBaseline()?.Units ?? []).Append(new BaselineUnit
            {
                EntryId = entry, Language = "fr", Remote = new SyncRemoteKey("default", key, "id-" + key), Value = value, ValueHash = BaselineUnit.Hash(value), RemoteRevision = revision,
            })],
        });

    // ───────────── the normal path ─────────────

    [Fact]
    public async Task AppliedPushAdvancesTheBaselineAndEmptiesTheJournal()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);

        var result = await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Confirmed);
        Assert.Equal("new", _session.Units[("a", "fr")].Value);
        var baseline = Assert.Single(_store.LoadBaseline()!.Units);
        Assert.Equal(("new", _session.Units[("a", "fr")].Revision), (baseline.Value, baseline.RemoteRevision));
        Assert.Empty(_store.LoadJournal().Entries);
    }

    [Fact]
    public async Task ConflictIsReportedWithTheCurrentRemoteAndNothingIsOverwritten()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.Put("a", "fr", "theirs");

        var result = await PushAsync(PlanOf(Put(entry, "a", "mine", unit.Revision)));

        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(("old", "mine", "theirs"), (conflict.Base, conflict.Local, conflict.RemoteValue));
        Assert.Equal("theirs", _session.Units[("a", "fr")].Value);
        Assert.Equal("old", _store.LoadBaseline()!.Units[0].Value);
        Assert.Equal(JournalState.Conflict, Assert.Single(_store.LoadJournal().Entries).State);
    }

    [Fact]
    public async Task BatchesFollowTheServiceLimit()
    {
        var plan = PlanOf([.. Enumerable.Range(0, 5).Select(i =>
        {
            var key = "k" + i;
            var unit = _session.Put(key, "fr", "old");
            var entry = Guid.NewGuid();
            Bind(entry, key, "old", unit.Revision!);
            return Put(entry, key, "new" + i, unit.Revision);
        })]);

        var result = await PushAsync(plan, new SourceLimits(MaxBatchSize: 2, MaxConcurrency: 1));

        Assert.Equal(5, result.Confirmed);
        Assert.Equal(3, _session.WriteCalls);
    }

    [Fact]
    public async Task ExcludedChangesAreNotSent()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        var plan = PlanOf(Put(entry, "a", "new", unit.Revision) with { Include = false });
        var result = await PushAsync(plan);
        Assert.Equal(0, result.Confirmed);
        Assert.Equal(0, _session.WriteCalls);
    }

    [Fact]
    public async Task CreatedKeysAreSentOnceWithAllTheirLanguagesAndBoundWithTheAssignedId()
    {
        var en = Guid.NewGuid();
        var fr = Guid.NewGuid();
        var remote = new SyncRemoteKey("default", "new.key");
        var plan = PlanOf(
            new PushChange { Kind = PushKind.Create, EntryId = en, Language = "fr", Remote = remote, Value = "Bonjour", Values = new Dictionary<string, string> { ["fr"] = "Bonjour" } },
            new PushChange { Kind = PushKind.Create, EntryId = fr, Language = "fr", Remote = remote, Value = "Salut", Values = new Dictionary<string, string> { ["fr"] = "Salut" } });

        var result = await PushAsync(plan);

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(1, _session.AppliedCount);
        Assert.Equal("id-new.key", _store.LoadBaseline()!.Units.First().Remote.RemoteId);
    }

    // ───────────── ambiguous failures and restart ─────────────

    [Fact]
    public async Task TimeoutAfterCommitIsConfirmedByReadingNeverSentTwice()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowAfterApplying = new TimeoutException("reset");

        var first = await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));
        Assert.Equal(1, first.Unknown);
        Assert.Equal(JournalState.Unknown, Assert.Single(_store.LoadJournal().Entries).State);

        var second = await ResumeAsync();

        Assert.Equal(1, second.Confirmed);
        Assert.Equal(1, _session.WriteCalls);
        Assert.Empty(_store.LoadJournal().Entries);
        Assert.Equal("new", _store.LoadBaseline()!.Units[0].Value);
    }

    [Fact]
    public async Task TimeoutBeforeCommitIsResentWithTheSameOperationId()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowBeforeApplying = new HttpRequestException("down");

        await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));
        var again = await ResumeAsync();

        Assert.Equal(1, again.Confirmed);
        Assert.Equal(2, _session.WriteCalls);
        Assert.Single(_session.SentOperationIds.Distinct());
        Assert.Equal("new", _session.Units[("a", "fr")].Value);
    }

    [Fact]
    public async Task UnclearOperationBecomesAConflictWhenSomeoneElseChangedTheValue()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowBeforeApplying = new HttpRequestException("down");
        await PushAsync(PlanOf(Put(entry, "a", "mine", unit.Revision)));
        _session.Put("a", "fr", "theirs");

        var result = await ResumeAsync();

        Assert.Single(result.Conflicts);
        Assert.Equal(1, _session.WriteCalls);
        Assert.Equal("theirs", _session.Units[("a", "fr")].Value);
    }

    [Fact]
    public async Task RestartFromInFlightReconcilesBeforeAnythingIsSent()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "applied");
        Bind(entry, "a", "old", unit.Revision!);
        var op = Guid.NewGuid();
        _store.SaveJournal(new SyncJournal
        {
            Entries = [new() { OperationId = op, Kind = JournalKind.PutValue, EntryId = entry, Language = "fr", RemoteLanguage = "fr", Target = new SyncRemoteKey("default", "a", "id-a"),
                Payload = "applied", BaseValue = "old", State = JournalState.InFlight, Attempts = 1 }],
        });

        var result = await ResumeAsync();

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(0, _session.WriteCalls);
        Assert.Equal("applied", _store.LoadBaseline()!.Units[0].Value);
    }

    [Fact]
    public async Task ACreateThatMayHaveLandedIsNotDuplicated()
    {
        var entry = Guid.NewGuid();
        var plan = PlanOf(new PushChange { Kind = PushKind.Create, EntryId = entry, Language = "fr", Remote = new SyncRemoteKey("default", "n"), Value = "v", Values = new Dictionary<string, string> { ["fr"] = "v" } });
        _session.ThrowAfterApplying = new TimeoutException();

        await PushAsync(plan);
        var result = await ResumeAsync();

        Assert.Equal(1, result.Confirmed);
        Assert.Single(_session.Units.Keys, k => k.Name == "n");
        Assert.Equal(1, _session.WriteCalls);
    }

    [Fact]
    public async Task ADroppedPageDuringRecoveryLeavesUnclearOperationsAlone()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowAfterApplying = new TimeoutException();
        await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));

        _session.ReadsAreIncomplete = true;
        var result = await ResumeAsync();

        Assert.Equal(1, result.Unknown);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(1, _session.WriteCalls);
        Assert.Equal(JournalState.Unknown, Assert.Single(_store.LoadJournal().Entries).State);
    }

    [Fact]
    public async Task CancellationMidBatchLeavesInFlightEntriesForTheNextRun()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        using var cts = new CancellationTokenSource();
        _session.OnWrite = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)), ct: cts.Token));

        Assert.Equal(JournalState.InFlight, Assert.Single(_store.LoadJournal().Entries).State);
        var resumed = await ResumeAsync();
        Assert.Equal(1, resumed.Confirmed);
        Assert.Equal("new", _session.Units[("a", "fr")].Value);
    }

    // ───────────── service behavior ─────────────

    [Fact]
    public async Task RateLimitIsRetriedAfterTheServiceAsked()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowBeforeApplying = new SourceException(new SourceError(SourceErrorKind.RateLimited, "slow") { RetryAfter = TimeSpan.FromMilliseconds(10) });

        var result = await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));

        Assert.True(result.Succeeded);
        Assert.Equal(2, _session.WriteCalls);
    }

    [Fact]
    public async Task ExpiredSignInStopsTheRunAndKeepsTheChangesPending()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.ThrowBeforeApplying = new SourceException(new SourceError(SourceErrorKind.Authentication, "expired"));

        var result = await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));

        Assert.Equal(1, result.Failed);
        Assert.Contains("expired", result.Errors);
        Assert.Equal(JournalState.Failed, Assert.Single(_store.LoadJournal().Entries).State);
        Assert.Equal("old", _session.Units[("a", "fr")].Value);
    }

    [Fact]
    public async Task AsynchronousWritesAreConfirmedWhenTheJobEnds()
    {
        var entry = Guid.NewGuid();
        var unit = _session.Put("a", "fr", "old");
        Bind(entry, "a", "old", unit.Revision!);
        _session.AsyncJobPolls = 2;

        var result = await PushAsync(PlanOf(Put(entry, "a", "new", unit.Revision)));

        Assert.True(result.Succeeded);
        Assert.Equal("new", _store.LoadBaseline()!.Units[0].Value);
        Assert.Empty(_store.LoadJournal().Entries);
    }
}
