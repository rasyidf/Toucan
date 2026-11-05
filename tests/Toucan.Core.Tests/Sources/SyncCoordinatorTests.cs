using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Modules;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>The whole loop through the coordinator: connect, first sync, offline edits, push, conflicts, sign-in loss.</summary>
public sealed class SyncCoordinatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-coord-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly ScriptedUnitSession _session = new();
    private readonly ISourceConnectionService _connections = Substitute.For<ISourceConnectionService>();
    private readonly SyncCoordinator _coordinator;
    private readonly List<SourceConnectionSettings> _settings = [new() { Id = "c1", Name = "Service", Adapter = "test.adapter", Languages = { ["fr"] = "fr" } }];

    private sealed class Adapter : IContentSourceAdapter
    {
        public SourceAdapterDescriptor Descriptor { get; } = new()
        {
            Id = "test.adapter", DisplayName = "Test", DefaultBaseUrl = new Uri("https://x.test/"), AuthMethods = [new ApiTokenAuth("t", "Token")],
            Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.ConditionalWrites | SourceCapabilities.DeleteKeys,
        };
        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Catalog : ISourceAdapterCatalog
    {
        private readonly RegisteredSourceAdapter _adapter = new("test.plugin", new Adapter());
        public IReadOnlyList<RegisteredSourceAdapter> Adapters => [_adapter];
        public RegisteredSourceAdapter? Find(string adapterId) => adapterId == "test.adapter" ? _adapter : null;
    }

    public SyncCoordinatorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _services.GetRequiredService<ITranslationManagementService>().Initialize([
            new() { Namespace = "hello", Language = "fr", Value = "Bonjour" },
            new() { Namespace = "bye", Language = "fr", Value = "Au revoir" },
        ]);
        _workspace.Open(new ProjectSettings { ProjectPath = _root, PrimaryLanguage = "fr", Languages = ["fr"] });
        _session.Put("hello", "fr", "Bonjour");
        _session.Put("bye", "fr", "Salut");
        _connections.GetSessionAsync("c1", Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IContentSourceSession>(_session));
        _coordinator = new SyncCoordinator(_workspace, _connections, new Catalog()) { DeliveryDebounce = TimeSpan.FromMilliseconds(5) };
        _coordinator.Open(_root, _settings);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _workspace.Dispose();
        _services.Dispose();
        TempFolder.TryDelete(_root);
    }

    private async Task<string> ValueAsync(string key) => (await _workspace.SnapshotEntriesAsync())!.Units.Single(u => u.Key == key).Value;

    private async Task EditAsync(string key, string value) =>
        Assert.True((await _workspace.ApplyAsync("test", new WorkspaceEdit().SetValue(key, "fr", value))).Applied);

    private async Task SettleFirstSyncAsync()
    {
        var plan = await _coordinator.PlanAsync("c1", SyncPlanKind.Sync);
        plan = SyncPlanner.ResolveAll(plan, SyncChoice.TakeRemote);
        await _coordinator.ApplyAsync(plan);
    }

    [Fact]
    public async Task FirstSyncNeverMergesSilently()
    {
        var result = await _coordinator.RunAsync("c1", SyncPlanKind.Sync);

        Assert.True(result.Plan.IsFirstSync);
        var conflict = Assert.Single(result.Plan.Conflicts);
        Assert.Equal("bye", conflict.Remote.Name);
        Assert.Equal("Au revoir", await ValueAsync("bye"));
        Assert.Equal("Salut", _session.Units[("bye", "fr")].Value);
        Assert.Equal(1, _coordinator.Status("c1")!.Conflicts);
    }

    [Fact]
    public async Task RequireNoConflictsAppliesNothing()
    {
        var result = await _coordinator.RunAsync("c1", SyncPlanKind.Sync, requireNoConflicts: true);
        Assert.False(result.Applied);
        Assert.False(new SyncStateStore(_root, "c1").HasBaseline);
    }

    [Fact]
    public async Task ChoosingTheRemoteSideBindsEverythingAndSettlesDelivery()
    {
        await SettleFirstSyncAsync();

        Assert.Equal("Salut", await ValueAsync("bye"));
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        Assert.All(snapshot.Units, u => Assert.Equal(SourceDeliveryState.Synced, u.Delivery));
        Assert.Equal(2, new SyncStateStore(_root, "c1").LoadBaseline()!.Units.Count);
    }

    [Fact]
    public async Task OfflineEditsShowAsPendingThenSyncedAfterAPush()
    {
        await SettleFirstSyncAsync();
        await EditAsync("hello", "Coucou");
        var hello = (await _workspace.SnapshotEntriesAsync())!.Units.Single(u => u.Key == "hello");
        _coordinator.RefreshDelivery(publish: false);
        Assert.Equal(SourceDeliveryState.Pending, _coordinator.DeliveryOf(hello.EntryId, "fr"));

        var result = await _coordinator.RunAsync("c1", SyncPlanKind.Push);

        Assert.True(result.Succeeded);
        Assert.Equal("Coucou", _session.Units[("hello", "fr")].Value);
        Assert.Equal(SourceDeliveryState.Synced, _coordinator.DeliveryOf(hello.EntryId, "fr"));
        Assert.NotNull(_coordinator.Status("c1")!.LastPush);
    }

    [Fact]
    public async Task ARemoteEditIsPulledAndAConcurrentLocalEditConflicts()
    {
        await SettleFirstSyncAsync();
        _session.Put("hello", "fr", "Theirs");
        var pulled = await _coordinator.RunAsync("c1", SyncPlanKind.Pull);
        Assert.Equal(1, pulled.Pull!.Applied);
        Assert.Equal("Theirs", await ValueAsync("hello"));

        await EditAsync("hello", "Mine");
        _session.Put("hello", "fr", "Also theirs");
        var plan = await _coordinator.PlanAsync("c1", SyncPlanKind.Sync);
        Assert.Equal(("Theirs", "Mine", "Also theirs"), (plan.Conflicts[0].Base, plan.Conflicts[0].Local, plan.Conflicts[0].RemoteValue));
    }

    [Fact]
    public async Task ADeletionHereIsOfferedButNotSentUntilIncluded()
    {
        await SettleFirstSyncAsync();
        Assert.True((await _workspace.ApplyAsync("test", new WorkspaceEdit().DeleteKey("bye"))).Applied);

        var plan = await _coordinator.PlanAsync("c1", SyncPlanKind.Push);
        var delete = Assert.Single(plan.Push);
        Assert.False(delete.Include);

        await _coordinator.ApplyAsync(plan);
        Assert.Contains(_session.Units.Keys, k => k.Name == "bye");

        await _coordinator.ApplyAsync(plan.Include(delete.Id));
        Assert.DoesNotContain(_session.Units.Keys, k => k.Name == "bye");
    }

    [Fact]
    public async Task RequestsForABusyConnectionJoinTheRunInProgress()
    {
        await SettleFirstSyncAsync();
        var first = _coordinator.RunAsync("c1", SyncPlanKind.Sync);
        var second = _coordinator.RunAsync("c1", SyncPlanKind.Sync);
        Assert.Same(first, second);
        await first;
    }

    [Fact]
    public async Task LostSignInIsReportedAsNeedingSignInNotAsAFailure()
    {
        _connections.GetSessionAsync("c1", Arg.Any<CancellationToken>()).Returns(Task.FromException<IContentSourceSession>(
            new SourceException(new SourceError(SourceErrorKind.Authentication, "expired"))));
        var events = new List<SyncStatus>();
        _coordinator.StatusChanged += (_, e) => events.Add(e.Status);

        await Assert.ThrowsAsync<SourceException>(() => _coordinator.RunAsync("c1", SyncPlanKind.Sync));

        Assert.True(_coordinator.Status("c1")!.NeedsSignIn);
        Assert.Contains(events, s => s.State == SyncRunState.Planning);
        Assert.Equal(SyncRunState.Idle, _coordinator.Status("c1")!.State);
    }

    [Fact]
    public async Task ASessionThatReadsNeitherUnitsNorDocumentsIsRefusedWithAClearMessage()
    {
        _connections.GetSessionAsync("c1", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IContentSourceSession>(Substitute.For<IContentSourceSession>()));
        var failure = await Assert.ThrowsAsync<SourceException>(() => _coordinator.PlanAsync("c1", SyncPlanKind.Pull));
        Assert.Equal(SourceErrorKind.Unsupported, failure.Error.Kind);
    }

    [Fact]
    public async Task ScheduledChecksPlanWithoutApplying()
    {
        await SettleFirstSyncAsync();
        _session.Put("hello", "fr", "Waiting");
        Assert.Throws<ArgumentOutOfRangeException>(() => _coordinator.StartScheduledCheck("c1", TimeSpan.FromSeconds(1)));

        var plan = await _coordinator.PlanAsync("c1", SyncPlanKind.Pull);

        Assert.Equal(1, plan.PullCount);
        Assert.True(_coordinator.Status("c1")!.RemoteChangesAvailable);
        Assert.Equal("Bonjour", await ValueAsync("hello"));
    }

    [Fact]
    public async Task ClosingTheProjectClearsStatusAndDeliveryProvider()
    {
        await SettleFirstSyncAsync();
        _coordinator.Close();
        Assert.Empty(_coordinator.Statuses);
        Assert.Null(_workspace.DeliveryProvider);
    }
}
