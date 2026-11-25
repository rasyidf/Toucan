using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Modules;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>The state store, pulls through the workspace, delivery state and connection rules.</summary>
public sealed class SyncStateAndPullTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-pull-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly SyncStateStore _store;
    private readonly SourceConnectionSettings _connection = new() { Id = "c1", Name = "Service", Adapter = "x.y", Languages = { ["en"] = "en", ["fr"] = "fr" } };

    public SyncStateAndPullTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _services.GetRequiredService<ITranslationManagementService>().Initialize([
            new() { Namespace = "hello", Language = "en", Value = "Hello" },
            new() { Namespace = "hello", Language = "fr", Value = "Bonjour" },
            new() { Namespace = "bye", Language = "en", Value = "Bye" },
            new() { Namespace = "bye", Language = "fr", Value = "Au revoir" },
        ]);
        _workspace.Open(new ProjectSettings { ProjectPath = _root, PrimaryLanguage = "en", Languages = ["en", "fr"] });
        _store = new SyncStateStore(_root, "c1");
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
        TempFolder.TryDelete(_root);
    }

    private async Task<IdentifiedWorkspaceSnapshot> SnapshotAsync() => (await _workspace.SnapshotEntriesAsync())!;

    private static PullChange Update(IdentifiedUnitSnapshot unit, string value, string revision = "5") => new()
    {
        Kind = PullKind.Update, Field = SyncField.Value, EntryId = unit.EntryId, Language = unit.Language, Remote = new SyncRemoteKey("default", unit.Key),
        Namespace = unit.Namespace, KeyPath = unit.KeyPath, LocalValue = unit.Value, NewValue = value, RemoteRevision = revision,
    };

    private static SyncPlan PlanOf(params PullChange[] pulls) =>
        new("c1", Guid.NewGuid(), SyncPlanKind.Pull, 1, DateTimeOffset.UnixEpoch, true, false, pulls, [], [], []);

    // ───────────── state store ─────────────

    [Fact]
    public void StateLivesUnderTheLocalFolderWithAnIgnoreFile()
    {
        _store.SaveBaseline(new SyncBaseline { Units = [new() { EntryId = Guid.NewGuid(), Language = "fr", Value = "x" }] });
        Assert.True(File.Exists(Path.Combine(_root, ".toucan", "local", "sources", "c1", "baseline.json")));
        Assert.Equal("*\n", File.ReadAllText(Path.Combine(_root, ".toucan", "local", ".gitignore")));
        Assert.True(_store.HasBaseline);
        Assert.Single(_store.LoadBaseline()!.Units);
    }

    [Fact]
    public void ACorruptBaselineIsKeptAsideAndTreatedAsAFirstSync()
    {
        Directory.CreateDirectory(_store.Folder);
        File.WriteAllText(_store.BaselinePath, "{ not json");

        Assert.Null(_store.LoadBaseline());

        Assert.False(File.Exists(_store.BaselinePath));
        Assert.Single(Directory.GetFiles(_store.Folder, "baseline.json.corrupt-*"));
    }

    [Fact]
    public void ABaselineFromANewerToucanIsNotOverwritten()
    {
        Directory.CreateDirectory(_store.Folder);
        File.WriteAllText(_store.BaselinePath, "{\"Schema\": 99, \"Units\": []}");

        Assert.Null(_store.LoadBaseline());
        Assert.Single(Directory.GetFiles(_store.Folder, "baseline.json.corrupt-*"));
    }

    [Fact]
    public void JournalAndCacheRoundTrip()
    {
        _store.SaveJournal(new SyncJournal { Entries = [new() { OperationId = Guid.NewGuid(), Kind = JournalKind.CreateKey, State = JournalState.Unknown, Values = new() { ["fr"] = "v" }, Members = [new() { EntryId = Guid.NewGuid(), Language = "fr" }] }] });
        var journal = _store.LoadJournal();
        Assert.Equal((JournalKind.CreateKey, JournalState.Unknown), (journal.Entries[0].Kind, journal.Entries[0].State));
        Assert.Equal("v", journal.Entries[0].Values!["fr"]);

        _store.SaveUnitCache([new RemoteUnit(new RemoteKey("default", "a"), "fr", "x") { Revision = "3", Status = new RemoteReviewStatus("ok", SourceReviewMeaning.Approved) }]);
        var cached = Assert.Single(_store.LoadUnitCache()!);
        Assert.Equal(("a", "3", SourceReviewMeaning.Approved), (cached.Key.Name, cached.Revision, cached.Status!.Meaning));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    public void ConnectionIdsCannotEscapeTheStateFolder(string id) =>
        Assert.Throws<ArgumentException>(() => new SyncStateStore(_root, id));

    [Fact]
    public void DeleteForgetsTheConnection()
    {
        _store.SaveBaseline(new SyncBaseline());
        _store.Delete();
        Assert.False(_store.HasBaseline);
    }

    // ───────────── pulls ─────────────

    [Fact]
    public async Task PullChangesTheProjectAsOneSourceTransactionAndUndoRestoresIt()
    {
        var before = await SnapshotAsync();
        var hello = before.Units.Single(u => u.Key == "hello" && u.Language == "fr");
        var bye = before.Units.Single(u => u.Key == "bye" && u.Language == "fr");
        var origins = new List<(WorkspaceChangeOrigin, string?)>();
        await using var subscription = (await _workspace.SubscribeAsync())!;
        var reader = Task.Run(async () =>
        {
            await foreach (var change in subscription.Changes) { origins.Add((change.Origin, change.OriginId)); break; }
        });

        var result = await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(Update(hello, "Salut"), Update(bye, "Ciao")), _connection, _store);
        await reader;

        Assert.Equal((2, 0, null), (result.Applied, result.Created, result.Refusal));
        var after = await SnapshotAsync();
        Assert.Equal("Salut", after.Units.Single(u => u.EntryId == hello.EntryId && u.Language == "fr").Value);
        Assert.Equal([(WorkspaceChangeOrigin.Source, "c1")], origins);
        Assert.True(_workspace.IsDirty);

        Assert.True(_workspace.Undo());
        var undone = await SnapshotAsync();
        Assert.Equal("Bonjour", undone.Units.Single(u => u.EntryId == hello.EntryId && u.Language == "fr").Value);
        Assert.Equal("Au revoir", undone.Units.Single(u => u.EntryId == bye.EntryId && u.Language == "fr").Value);
    }

    [Fact]
    public async Task OnlyAppliedUnitsAdvanceTheBaseline()
    {
        var snap = await SnapshotAsync();
        var hello = snap.Units.Single(u => u.Key == "hello" && u.Language == "fr");
        var bye = snap.Units.Single(u => u.Key == "bye" && u.Language == "fr");
        // The user types into "bye" after the plan was made.
        var typed = Update(bye, "Ciao") with { LocalValue = "something older" };

        var result = await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(Update(hello, "Salut", "7"), typed), _connection, _store);

        Assert.Equal(1, result.Applied);
        Assert.Equal(typed.Id, Assert.Single(result.Skipped).Id);
        Assert.Equal("Au revoir", (await SnapshotAsync()).Units.Single(u => u.EntryId == bye.EntryId && u.Language == "fr").Value);
        var baseline = Assert.Single(_store.LoadBaseline()!.Units);
        Assert.Equal((hello.EntryId, "Salut", "7"), (baseline.EntryId, baseline.Value, baseline.RemoteRevision));
    }

    [Fact]
    public async Task ConvergedUnitsOnlyMoveTheBaseline()
    {
        var hello = (await SnapshotAsync()).Units.Single(u => u.Key == "hello" && u.Language == "fr");
        var converge = Update(hello, "Bonjour", "3") with { Kind = PullKind.Converge };

        var result = await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(converge), _connection, _store);

        Assert.Equal(0, result.Applied);
        Assert.False(_workspace.IsDirty);
        Assert.Equal("3", Assert.Single(_store.LoadBaseline()!.Units).RemoteRevision);
    }

    [Fact]
    public async Task NewRemoteKeysAreAddedAndBound()
    {
        var create = new PullChange
        {
            Kind = PullKind.Create, Field = SyncField.Existence, Language = "fr", Remote = new SyncRemoteKey("default", "brand.new", "k9"),
            KeyPath = new TranslationPath(["brand", "new"]), NewValue = "Nouveau", RemoteRevision = "4", NewValues = new Dictionary<string, string> { ["fr"] = "Nouveau", ["en"] = "New" },
        };

        var result = await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(create), _connection, _store);

        Assert.Equal(1, result.Created);
        var after = await SnapshotAsync();
        Assert.Equal("Nouveau", after.Units.Single(u => u.Key == "brand.new" && u.Language == "fr").Value);
        Assert.Equal(2, _store.LoadBaseline()!.Units.Count(u => u.Remote.RemoteId == "k9"));
    }

    [Fact]
    public async Task DeletesAreAppliedOnlyWhenIncluded()
    {
        var bye = (await SnapshotAsync()).Units.Single(u => u.Key == "bye" && u.Language == "fr");
        var delete = Update(bye, "") with { Kind = PullKind.Delete, Field = SyncField.Existence, Include = false };

        await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(delete), _connection, _store);
        Assert.Contains((await SnapshotAsync()).Units, u => u.Key == "bye");

        await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(delete with { Include = true }), _connection, _store);
        Assert.DoesNotContain((await SnapshotAsync()).Units, u => u.Key == "bye");
    }

    [Fact]
    public async Task PulledStatusMapsToTheReviewState()
    {
        var hello = (await SnapshotAsync()).Units.Single(u => u.Key == "hello" && u.Language == "fr");
        var status = Update(hello, "") with { Field = SyncField.Status, NewValue = null, NewStatus = SourceReviewMeaning.Approved };

        await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(status), _connection, _store);

        Assert.Equal(ReviewState.Approved, (await SnapshotAsync()).Units.Single(u => u.EntryId == hello.EntryId && u.Language == "fr").Review);
    }

    [Fact]
    public async Task ARefusedPulledApprovalDoesNotDropTheOtherReviewStates()
    {
        Assert.True(_workspace.Apply("editor", new WorkspaceEdit().SetValue("bye", "fr", "")).Applied);
        var units = (await SnapshotAsync()).Units;
        var hello = units.Single(u => u.Key == "hello" && u.Language == "fr");
        var bye = units.Single(u => u.Key == "bye" && u.Language == "fr");
        PullChange Approve(IdentifiedUnitSnapshot unit) =>
            Update(unit, "") with { Field = SyncField.Status, NewValue = null, LocalValue = null, NewStatus = SourceReviewMeaning.Approved };

        var result = await new SyncPullApplier(_workspace).ApplyAsync(PlanOf(Approve(hello), Approve(bye)), _connection, _store);

        var after = (await SnapshotAsync()).Units;
        Assert.Equal(ReviewState.Approved, after.Single(u => u.EntryId == hello.EntryId && u.Language == "fr").Review);
        Assert.NotEqual(ReviewState.Approved, after.Single(u => u.EntryId == bye.EntryId && u.Language == "fr").Review);
        Assert.Contains(result.Skipped, c => c.EntryId == bye.EntryId);
    }

    // ───────────── delivery ─────────────

    [Fact]
    public async Task DeliveryComesFromTheJournalAndTheBaseline()
    {
        var snap = await SnapshotAsync();
        var fr = snap.Units.Where(u => u.Language == "fr").ToList();
        var synced = fr[0];
        var changed = fr[1];
        var failed = snap.Units.First(u => u.Language == "en");
        var baseline = new SyncBaseline { Units = [new() { EntryId = synced.EntryId, Language = "fr", Value = synced.Value, ValueHash = BaselineUnit.Hash(synced.Value) },
            new() { EntryId = changed.EntryId, Language = "fr", Value = "old", ValueHash = BaselineUnit.Hash("old") }] };
        var journal = new SyncJournal { Entries = [new() { Kind = JournalKind.PutValue, EntryId = failed.EntryId, Language = "en", State = JournalState.Failed }] };
        var local = SyncMapping.LocalUnits(snap);

        var states = SyncDelivery.Compute(baseline, journal, local);

        Assert.Equal(SourceDeliveryState.Synced, states[(synced.EntryId, "fr")]);
        Assert.Equal(SourceDeliveryState.Pending, states[(changed.EntryId, "fr")]);
        Assert.Equal(SourceDeliveryState.Failed, states[(failed.EntryId, "en")]);
    }

    [Theory]
    [InlineData(JournalState.InFlight, SourceDeliveryState.Sending)]
    [InlineData(JournalState.Unknown, SourceDeliveryState.Sending)]
    [InlineData(JournalState.Conflict, SourceDeliveryState.Conflict)]
    public void JournalStatesMapToDeliveryStates(JournalState journalState, SourceDeliveryState expected)
    {
        var id = Guid.NewGuid();
        var states = SyncDelivery.Compute(null, new SyncJournal { Entries = [new() { Kind = JournalKind.PutValue, EntryId = id, Language = "fr", State = journalState }] },
            [new SyncLocalUnit(id, "fr", TranslationPath.Empty, new TranslationPath(["a"]), "x", "", SourceReviewMeaning.Draft)]);
        Assert.Equal(expected, states[(id, "fr")]);
    }

    [Fact]
    public async Task SnapshotsAndDeltasCarryTheDeliveryState()
    {
        var hello = (await SnapshotAsync()).Units.Single(u => u.Key == "hello" && u.Language == "fr");
        _workspace.DeliveryProvider = (id, language) => id == hello.EntryId && language == "fr" ? SourceDeliveryState.Failed : null;
        Assert.Equal(SourceDeliveryState.Failed, (await SnapshotAsync()).Units.Single(u => u.EntryId == hello.EntryId && u.Language == "fr").Delivery);

        await using var subscription = (await _workspace.SubscribeAsync())!;
        _workspace.PublishDeliveryChanged([(hello.EntryId, "fr")]);
        await foreach (var change in subscription.Changes)
        {
            Assert.Equal(WorkspaceChangeKind.DeliveryChanged, change.Kind);
            Assert.Equal(SourceDeliveryState.Failed, Assert.Single(change.Units).Unit!.Delivery);
            break;
        }
    }

    // ───────────── connections ─────────────

    [Fact]
    public void TwoPrimaryConnectionsForTheSameContentAreRefused()
    {
        SourceConnectionSettings Primary(string id, string package) => new() { Id = id, Adapter = "x.y", Role = SourceRoles.Primary, Languages = { ["fr"] = "fr" }, Resources = [new() { Remote = id, Package = package }] };

        Assert.Empty(SyncCoordinator.Validate([Primary("a", "app"), Primary("b", "web")]));
        Assert.Single(SyncCoordinator.Validate([Primary("a", "app"), Primary("b", "app")]));
        Assert.Empty(SyncCoordinator.Validate([_connection, new SourceConnectionSettings { Id = "m", Adapter = "x.y", Languages = { ["fr"] = "fr" } }]));
        Assert.Single(SyncCoordinator.Validate([_connection, _connection.Clone()]));
    }
}
