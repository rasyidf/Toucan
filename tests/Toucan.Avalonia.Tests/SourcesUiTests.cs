using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Avalonia.Views.Panels;
using Toucan.Core.Commands;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>An in-memory unit service the desktop tests sync with, reached through a normal adapter registration.</summary>
internal sealed class MemoryUnitService
{
    private readonly Dictionary<string, (string Value, int Revision)> _units = [];
    private int _revision;

    public void Put(string key, string language, string value) => _units[key + "|" + language] = (value, ++_revision);
    public string? Get(string key, string language) => _units.TryGetValue(key + "|" + language, out var u) ? u.Value : null;
    public IEnumerable<(string Key, string Language, string Value, int Revision)> All => _units.Select(p => (p.Key.Split('|')[0], p.Key.Split('|')[1], p.Value.Value, p.Value.Revision));

    public int? Write(string key, string language, string value, string? expected)
    {
        var slot = key + "|" + language;
        if (expected is not null && (!_units.TryGetValue(slot, out var current) || current.Revision.ToString() != expected)) return null;
        _units[slot] = (value, ++_revision);
        return _revision;
    }
}

internal sealed class MemoryUnitAdapter(MemoryUnitService service) : IContentSourceAdapter
{
    public const string Id = "test.memory";
    private const SourceCapabilities Caps = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.ConditionalWrites;

    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = Id, DisplayName = "Memory service", DefaultBaseUrl = new Uri("http://localhost/"), AuthMethods = [new NoSignInAuth()], Capabilities = Caps,
    };

    public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IContentSourceSession>(new Session(service));

    private sealed class Session(MemoryUnitService service) : IUnitSourceSession
    {
        public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) => Task.FromResult(SourceTestResult.Ok("Reachable."));

        public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SourceProjectInfo("mem", "Memory project", "en", [new("en"), new("fr"), new("fr-CA")], [new("default", "All strings")], Caps));

        public async IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new([.. service.All.Select(u => new RemoteUnit(new RemoteKey("default", u.Key), u.Language, u.Value) { Revision = u.Revision.ToString() })], null, true);
        }

        public Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken) =>
            Task.FromResult(new UnitWriteResult([.. batch.Operations.Select(op => op switch
            {
                PutValue put => service.Write(put.Key.Name, put.Language, put.Value, put.ExpectedRevision) is { } rev
                    ? new UnitOperationResult(op.OperationId, UnitOperationOutcome.Applied) { NewRevision = rev.ToString() }
                    : new UnitOperationResult(op.OperationId, UnitOperationOutcome.Conflict),
                _ => new UnitOperationResult(op.OperationId, UnitOperationOutcome.Refused),
            })]));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>The Sources panel, plan and conflict view, wizard, accounts page, indicators, badges and commands.</summary>
public sealed class SourcesUiTests : IDisposable
{
    private readonly MemoryUnitService _service = new();
    private readonly TestHost _host;
    private readonly SourceConnectionSettings _connection = new() { Id = "mem", Name = "Memory", Adapter = MemoryUnitAdapter.Id, Languages = { ["en"] = "en", ["fr"] = "fr" } };

    public SourcesUiTests() =>
        _host = new TestHost(configure: s => s.AddSingleton(new RegisteredSourceAdapter("test.plugin", new MemoryUnitAdapter(_service))));

    public void Dispose() => _host.Dispose();

    private async Task<MainWindowViewModel> OpenProjectAsync()
    {
        var folder = _host.CreateJsonProject("proj", ("en", """{"hello": "Hello", "bye": "Bye"}"""), ("fr", """{"hello": "Bonjour", "bye": "Au revoir"}"""));
        var vm = _host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        return vm;
    }

    private async Task ConnectAsync(MainWindowViewModel vm)
    {
        vm.ProjectSettings!.Sources.Add(_connection);
        await _host.Services.GetRequiredService<ISourceConnectionService>().AddAsync(_connection);
        vm.Sources!.Reload();
    }

    private static async Task<string?> ValueAsync(MainWindowViewModel vm, string key, string language) =>
        (await vm.Workspace.SnapshotEntriesAsync())!.Units.FirstOrDefault(u => u.Key == key && u.Language == language)?.Value;

    // ───────────── plan and conflict view models ─────────────

    private static SyncPlan PlanWith(IEnumerable<PullChange>? pull = null, IEnumerable<PushChange>? push = null, IEnumerable<SyncConflict>? conflicts = null, bool first = false) =>
        new("c", Guid.NewGuid(), SyncPlanKind.Sync, 1, DateTimeOffset.UnixEpoch, true, first, [.. pull ?? []], [.. push ?? []], [.. conflicts ?? []], [new(SyncNoticeKind.Info, "A note.")]);

    private static PullChange Pull(string key, string value, bool include = true) => new()
    {
        Kind = PullKind.Update, Field = SyncField.Value, EntryId = Guid.NewGuid(), Language = "fr", Remote = new SyncRemoteKey("default", key), NewValue = value, LocalValue = "old", Include = include,
    };

    private static PushChange Push(string key, string value) => new() { Kind = PushKind.Value, EntryId = Guid.NewGuid(), Language = "fr", Remote = new SyncRemoteKey("default", key), Value = value };

    private static SyncConflict Conflict(string key = "k") => new()
    {
        Kind = SyncConflictKind.BothChanged, Field = SyncField.Value, EntryId = Guid.NewGuid(), Language = "fr", Remote = new SyncRemoteKey("default", key), Base = "b", Local = "l", RemoteValue = "r",
    };

    [Fact]
    public void PlanDialogListsRowsAndSummarisesWhatIsIncluded()
    {
        var vm = new SyncPlanViewModel(PlanWith([Pull("a", "x"), Pull("b", "y", include: false)], [Push("c", "z")], [Conflict()]), "Memory");

        Assert.Equal(("Sync with Memory", 2, 1, 1), (vm.Title, vm.PullRows.Count, vm.PushRows.Count, vm.Conflicts.Count));
        Assert.True(vm.HasPull && vm.HasPush && vm.HasConflicts && vm.HasNotices);
        Assert.Equal("1 to pull · 1 to push · 1 conflicts left undecided", vm.Summary);
        Assert.Equal("Pull: Update a (fr)", vm.PullRows[0].AccessibleName);
    }

    [Fact]
    public void ExcludedRowsAndDecidedConflictsShapeThePlanToApply()
    {
        var vm = new SyncPlanViewModel(PlanWith([Pull("a", "x"), Pull("b", "y")], [Push("c", "z")], [Conflict("k1"), Conflict("k2")]), "M");
        vm.PullRows[1].Include = false;
        vm.PushRows[0].Include = false;
        vm.Conflicts[0].TakeRemoteCommand.Execute(null);
        vm.Conflicts[1].EditMergedCommand.Execute(null);
        vm.Conflicts[1].MergedText = "both";

        var plan = vm.BuildPlan();

        Assert.Equal(["x", "r", "both"], plan.Pull.Where(c => c.Include).Select(c => c.NewValue));
        Assert.Equal(["both"], plan.Push.Where(c => c.Include).Select(c => c.Value));
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void DeferredConflictsStayInThePlan()
    {
        var vm = new SyncPlanViewModel(PlanWith(conflicts: [Conflict()]), "M");
        Assert.Equal(SyncChoice.Defer, vm.Conflicts[0].Choice);
        Assert.Equal("Deferred", vm.Conflicts[0].ChoiceText);
        Assert.Single(vm.BuildPlan().Conflicts);
    }

    [Fact]
    public void BulkChoicesSettleEveryConflictAndReviewIndividuallyResetsThem()
    {
        var vm = new SyncPlanViewModel(PlanWith(conflicts: [Conflict("a"), Conflict("b")], first: true), "M");
        vm.UseLocalForAllCommand.Execute(null);
        Assert.All(vm.Conflicts, c => Assert.Equal(SyncChoice.TakeLocal, c.Choice));
        Assert.Equal(2, vm.BuildPlan().Push.Count);
        vm.UseRemoteForAllCommand.Execute(null);
        Assert.Equal(2, vm.BuildPlan().Pull.Count);
        vm.DeferAllCommand.Execute(null);
        Assert.Equal(2, vm.BuildPlan().Conflicts.Count);
        Assert.True(vm.IsFirstSync);
    }

    [Fact]
    public void ApplyAndCancelCloseTheDialogWithTheAnswer()
    {
        var vm = new SyncPlanViewModel(PlanWith([Pull("a", "x")]), "M");
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        vm.CancelCommand.Execute(null);
        Assert.False(vm.Confirmed);
        vm.ApplyCommand.Execute(null);
        Assert.True(vm.Confirmed);
        Assert.Equal(2, closed);
    }

    [Fact]
    public void ConflictKindsReadInPlainWords()
    {
        Assert.Equal("Changed here, deleted there", new ConflictRowViewModel(Conflict() with { Kind = SyncConflictKind.EditedLocallyDeletedRemotely }).KindText);
        Assert.Equal("Deleted here, changed there", new ConflictRowViewModel(Conflict() with { Kind = SyncConflictKind.DeletedLocallyEditedRemotely, Local = null }).KindText);
        Assert.Equal("(deleted)", new ConflictRowViewModel(Conflict() with { Local = null }).LocalText);
    }

    // ───────────── indicators and badges ─────────────

    [Fact]
    public void SavedAndSyncedAreSeparateIndicators()
    {
        var indicators = new SyncIndicators();
        indicators.UpdateSaved(isDirty: true);
        Assert.Equal("Unsaved changes", indicators.SavedText);
        indicators.UpdateSaved(isDirty: false);
        Assert.Equal("Saved locally", indicators.SavedText);

        indicators.UpdateSynced([]);
        Assert.False(indicators.HasConnections);
        SyncStatus Status(int pending = 0, int conflicts = 0, SyncRunState state = SyncRunState.Idle, bool signIn = false) =>
            new("c", "C", state, "", null, null, pending, conflicts, false, signIn);
        indicators.UpdateSynced([Status()]);
        Assert.Equal("Synced", indicators.SyncedText);
        indicators.UpdateSynced([Status(pending: 3)]);
        Assert.Equal("3 not synced", indicators.SyncedText);
        indicators.UpdateSynced([Status(conflicts: 2)]);
        Assert.Equal(("2 conflicts", true), (indicators.SyncedText, indicators.HasSyncProblem));
        indicators.UpdateSynced([Status(signIn: true)]);
        Assert.Equal("Sign in needed", indicators.SyncedText);
        indicators.UpdateSynced([Status(state: SyncRunState.Pushing)]);
        Assert.Equal(("Syncing…", true), (indicators.SyncedText, indicators.IsSyncing));
    }

    [AvaloniaFact]
    public async Task RowBadgesFollowTheDeliveryState()
    {
        var vm = await OpenProjectAsync();
        var row = vm.PagingController.Data.SelectMany(g => g.AllItems).First();
        Assert.False(row.HasDeliveryBadge);

        row.SetDelivery(SourceDeliveryState.Synced);
        Assert.False(row.HasDeliveryBadge);
        row.SetDelivery(SourceDeliveryState.Pending);
        Assert.True(row.IsDeliveryPending);
        row.SetDelivery(SourceDeliveryState.Failed);
        Assert.True(row.IsDeliveryFailed);
        Assert.Equal("Sending failed", row.DeliveryToolTip);
        row.SetDelivery(SourceDeliveryState.Conflict);
        Assert.True(row.IsDeliveryConflict);
        Assert.Equal("Conflict with the source", row.DeliveryToolTip);
    }

    // ───────────── the panel, end to end ─────────────

    [AvaloniaFact]
    public async Task PanelIsRegisteredAndCommandsAreInThePalette()
    {
        var vm = await OpenProjectAsync();
        App.RegisterSidePanels();
        Assert.NotNull(SidePanelRegistry.Instance.LeftSlotPanels.SingleOrDefault(p => p.Id == "sources"));
        Assert.True(vm.HasSources);

        using var registry = new CommandRegistry();
        BuiltInCommands.Bind(registry, null);
        foreach (var id in new[] { "pull", "push", "sync", "show-conflicts", "add-connection", "accounts" })
            Assert.Contains(BuiltInCommands.All, c => c.Id == $"toucan.sources.{id}");
        Assert.All(BuiltInCommands.All.Where(c => c.Category == "Sources"), c => Assert.Null(c.Shortcut));
    }

    [AvaloniaFact]
    public async Task ConnectionsAppearWithTheirStateAndNoConnectionOffersToAddOne()
    {
        var vm = await OpenProjectAsync();
        Assert.True(vm.Sources!.HasNoConnections);
        Assert.Equal("No connections", vm.Sources.Summary);

        await ConnectAsync(vm);

        var item = Assert.Single(vm.Sources.Connections);
        Assert.Equal(("Memory", "Memory service", "Ready"), (item.Name, item.AdapterName, item.StateText));
        Assert.True(item.CanSync);
        Assert.True(vm.Sources.HasConnections);
    }

    [AvaloniaFact]
    public async Task FirstPullShowsThePlanAndAppliesTheChosenSide()
    {
        var vm = await OpenProjectAsync();
        _service.Put("hello", "fr", "Salut");
        _service.Put("bye", "fr", "Au revoir");
        await ConnectAsync(vm);
        _host.Dialogs.OnSyncPlan = plan => { plan.UseRemoteForAllCommand.Execute(null); return true; };

        await vm.Sources!.PullCommand.ExecuteAsync(null);

        var shown = Assert.Single(_host.Dialogs.SyncPlans);
        Assert.True(shown.IsFirstSync);
        Assert.Equal("Salut", await ValueAsync(vm, "hello", "fr"));
        Assert.Equal("Au revoir", await ValueAsync(vm, "bye", "fr"));
        Assert.StartsWith("Pulled", vm.Sources.Connections[0].LastPullText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CancellingThePlanChangesNothing()
    {
        var vm = await OpenProjectAsync();
        _service.Put("hello", "fr", "Salut");
        await ConnectAsync(vm);
        _host.Dialogs.OnSyncPlan = plan => { plan.UseRemoteForAllCommand.Execute(null); return false; };

        await vm.Sources!.PullCommand.ExecuteAsync(null);

        Assert.Equal("Bonjour", await ValueAsync(vm, "hello", "fr"));
        Assert.False(new SyncStateStore(vm.ProjectSettings!.ProjectPath, "mem").HasBaseline);
    }

    [AvaloniaFact]
    public async Task NothingToDoSaysSoWithoutAPlanDialog()
    {
        var vm = await OpenProjectAsync();
        _service.Put("hello", "fr", "Bonjour");
        _service.Put("bye", "fr", "Au revoir");
        _service.Put("hello", "en", "Hello");
        _service.Put("bye", "en", "Bye");
        await ConnectAsync(vm);
        _host.Dialogs.OnSyncPlan = _ => true;
        await vm.Sources!.SyncNowCommand.ExecuteAsync(null);
        _host.Dialogs.SyncPlans.Clear();
        _host.Messages.Shown.Clear();

        await vm.Sources.SyncNowCommand.ExecuteAsync(null);

        Assert.Empty(_host.Dialogs.SyncPlans);
        Assert.Contains(_host.Messages.Shown, m => m.Contains("up to date", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task LocalEditsGoOutWithPushAndTheRowBadgeClears()
    {
        var vm = await OpenProjectAsync();
        foreach (var (key, lang, value) in new[] { ("hello", "fr", "Bonjour"), ("bye", "fr", "Au revoir"), ("hello", "en", "Hello"), ("bye", "en", "Bye") }) _service.Put(key, lang, value);
        await ConnectAsync(vm);
        await vm.Sources!.SyncNowCommand.ExecuteAsync(null);

        Assert.True((await vm.Workspace.ApplyAsync("test", new WorkspaceEdit().SetValue("hello", "fr", "Coucou"))).Applied);
        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        var entry = (await vm.Workspace.SnapshotEntriesAsync())!.Units.Single(u => u.Key == "hello" && u.Language == "fr");
        coordinator.RefreshDelivery(publish: true);
        Assert.Equal(SourceDeliveryState.Pending, coordinator.DeliveryOf(entry.EntryId, "fr"));
        Assert.Equal("1 not synced", vm.Sources.Connections[0].PendingText);

        await vm.Sources.PushCommand.ExecuteAsync(null);

        Assert.Equal("Coucou", _service.Get("hello", "fr"));
        Assert.Equal(SourceDeliveryState.Synced, coordinator.DeliveryOf(entry.EntryId, "fr"));
        Assert.Equal("Everything is synced", vm.Sources.Summary);
    }

    [AvaloniaFact]
    public async Task RemovingAConnectionAsksFirstAndForgetsItsState()
    {
        var vm = await OpenProjectAsync();
        _service.Put("hello", "fr", "Bonjour");
        await ConnectAsync(vm);
        _host.Dialogs.OnSyncPlan = _ => true;
        await vm.Sources!.SyncNowCommand.ExecuteAsync(null);
        var store = new SyncStateStore(vm.ProjectSettings!.ProjectPath, "mem");
        Assert.True(store.HasBaseline);

        _host.Messages.ConfirmAnswer = false;
        await vm.Sources.RemoveCommand.ExecuteAsync(null);
        Assert.Single(vm.Sources.Connections);

        _host.Messages.ConfirmAnswer = true;
        await vm.Sources.RemoveCommand.ExecuteAsync(null);
        Assert.Empty(vm.Sources.Connections);
        Assert.False(store.HasBaseline);
    }

    [AvaloniaFact]
    public async Task ExpiredSignInIsAStickyNotificationShownOnce()
    {
        var vm = await OpenProjectAsync();
        var center = _host.Services.GetRequiredService<INotificationCenter>();
        await ConnectAsync(vm);
        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        var before = center.History.Count;

        PublishStatus(coordinator, new SyncStatus("mem", "Memory", SyncRunState.Idle, "Sign in again to continue.", null, null, 0, 0, false, NeedsSignIn: true));
        PublishStatus(coordinator, new SyncStatus("mem", "Memory", SyncRunState.Idle, "Sign in again to continue.", null, null, 0, 0, false, NeedsSignIn: true));

        var added = center.History.Skip(before).Where(n => n.Content.Title.StartsWith("Sign in", StringComparison.Ordinal)).ToList();
        var note = Assert.Single(added);
        Assert.True(note.Content.Sticky);
        Assert.Equal("toucan.sources.accounts", note.Content.ActionCommandId);
        Assert.Equal("Not signed in", vm.Sources!.Connections[0].StateText);
    }

    [AvaloniaFact]
    public async Task PartialFailureAndWaitingRemoteChangesAreAnnounced()
    {
        var vm = await OpenProjectAsync();
        var center = _host.Services.GetRequiredService<INotificationCenter>();
        await ConnectAsync(vm);
        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        var before = center.History.Count;

        PublishStatus(coordinator, new SyncStatus("mem", "Memory", SyncRunState.Idle, "Some changes were not applied.", null, null, 2, 0, false, PartialFailure: true));
        PublishStatus(coordinator, new SyncStatus("mem", "Memory", SyncRunState.Idle, "Changes are ready.", null, null, 0, 0, true));

        var titles = center.History.Skip(before).Select(n => n.Content.Title).ToList();
        Assert.Contains("Some changes were not sent to Memory", titles);
        Assert.Contains("Memory has new changes", titles);
        Assert.Equal("toucan.sources.pull", center.History.Last().Content.ActionCommandId);
    }

    private static void PublishStatus(SyncCoordinator coordinator, SyncStatus status)
    {
        var method = typeof(SyncCoordinator).GetMethod("Publish", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(coordinator, [status]);
    }

    // ───────────── Save As ─────────────

    [AvaloniaFact]
    public async Task SaveAsKeepsTheConnectionsButStartsTheCopyAsAFirstSync()
    {
        var vm = await OpenProjectAsync();
        foreach (var (key, lang, value) in new[] { ("hello", "fr", "Bonjour"), ("bye", "fr", "Au revoir"), ("hello", "en", "Hello"), ("bye", "en", "Bye") }) _service.Put(key, lang, value);
        await ConnectAsync(vm);
        await vm.Sources!.SyncNowCommand.ExecuteAsync(null);
        var original = vm.ProjectSettings!.ProjectPath;
        Assert.True(new SyncStateStore(original, "mem").HasBaseline);

        var copy = Path.Combine(_host.Root, "copy");
        var lifecycle = _host.Services.GetRequiredService<IProjectLifecycleService>();
        var saved = await lifecycle.SaveProjectAsAsync(copy);
        Assert.True(saved.Status == ProjectSaveStatus.Success, saved.ErrorMessage);

        var coordinator = _host.Services.GetRequiredService<SyncCoordinator>();
        Assert.Equal(Path.GetFullPath(copy), coordinator.ProjectRoot);
        var settings = ProjectSettings.LoadFrom(copy)!;
        Assert.Equal("mem", Assert.Single(settings.Sources).Id);
        Assert.NotSame(settings.Sources[0], _connection);
        // The copy is a different set of files: it does not inherit what the original last agreed on with the service.
        Assert.False(new SyncStateStore(copy, "mem").HasBaseline);
        Assert.True(new SyncStateStore(original, "mem").HasBaseline);

        var plan = await coordinator.PlanAsync("mem", SyncPlanKind.Sync);
        Assert.True(plan.IsFirstSync);
        Assert.Empty(plan.Conflicts);
        Assert.NotEqual(SourceConnectionState.Failed, _host.Services.GetRequiredService<ISourceConnectionService>().Status("mem")!.State);
    }

    // ───────────── wizard ─────────────

    private AddConnectionViewModel Wizard(MainWindowViewModel vm) => new(_host.Services.GetRequiredService<SourceSignInService>(),
        _host.Services.GetRequiredService<ISourceConnectionService>(), _host.Services.GetRequiredService<ISourceAdapterCatalog>(), _host.Dialogs, _host.Messages,
        vm.ProjectSettings!.Sources, ["en", "fr"]);

    [AvaloniaFact]
    public async Task WizardWalksThroughTheStepsAndSavesTheConnection()
    {
        var vm = await OpenProjectAsync();
        var wizard = Wizard(vm);
        wizard.SelectedService = wizard.Services.Single(s => s.Descriptor.Id == MemoryUnitAdapter.Id);
        Assert.False(wizard.RequiresAccount);
        Assert.Equal("Choose a service", wizard.StepTitle);

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionStep.Project, wizard.Step);
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionStep.Mapping, wizard.Step);

        // Suggestions: exact matches are filled in, "fr-CA" is not guessed for a project language it does not equal.
        Assert.Equal(["en", "fr"], wizard.Languages.Select(l => l.Remote));
        Assert.All(wizard.Languages, l => Assert.True(l.IsExact));
        Assert.Equal("Memory project", wizard.ConnectionName);

        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionStep.Policy, wizard.Step);
        wizard.Role = SourceRoles.Primary;
        wizard.DeletePolicy = "never";
        await wizard.NextCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionStep.Test, wizard.Step);
        Assert.Equal("Reachable.", wizard.TestResult);
        Assert.True(wizard.IsLastStep);

        await wizard.FinishCommand.ExecuteAsync(null);

        var saved = Assert.Single(vm.ProjectSettings!.Sources);
        Assert.Same(saved, wizard.Saved);
        Assert.Equal((MemoryUnitAdapter.Id, SourceRoles.Primary, "never", "Memory project"), (saved.Adapter, saved.Role, saved.Policy.DeleteRemote, saved.Name));
        Assert.Equal(new Dictionary<string, string> { ["en"] = "en", ["fr"] = "fr" }, saved.Languages);
    }

    [AvaloniaFact]
    public async Task WizardRefusesAMappingThatMapsOneRemoteLanguageTwice()
    {
        var vm = await OpenProjectAsync();
        var wizard = Wizard(vm);
        wizard.SelectedService = wizard.Services.Single(s => s.Descriptor.Id == MemoryUnitAdapter.Id);
        await wizard.NextCommand.ExecuteAsync(null);
        await wizard.NextCommand.ExecuteAsync(null);
        wizard.Languages[0].Remote = "fr";

        await wizard.NextCommand.ExecuteAsync(null);

        Assert.Equal(ConnectionStep.Mapping, wizard.Step);
        Assert.True(wizard.HasProblems);
        Assert.Contains(wizard.Problems, p => p.Contains("map each remote language once", StringComparison.Ordinal));
        await wizard.CancelCommand.ExecuteAsync(null);
        Assert.Empty(vm.ProjectSettings!.Sources);
    }

    [AvaloniaFact]
    public async Task WizardRefusesKeysThatWouldCollideAtTheService()
    {
        var vm = await OpenProjectAsync();
        _connection.Keys.CaseSensitive = false;
        await ConnectAsync(vm);
        var wizard = Wizard(vm);
        wizard.ProjectKeyPaths = () => [["Hello"], ["hello"]];
        wizard.Edit(_connection);
        await Task.Delay(50);

        await wizard.NextCommand.ExecuteAsync(null);

        Assert.Equal(ConnectionStep.Mapping, wizard.Step);
        Assert.Contains(wizard.Problems, p => p.Contains("same remote key", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task EditingAnExistingConnectionStartsAtTheMapping()
    {
        var vm = await OpenProjectAsync();
        await ConnectAsync(vm);
        var wizard = Wizard(vm);
        wizard.Edit(_connection);

        Assert.True(wizard.IsEditing);
        Assert.Equal(ConnectionStep.Mapping, wizard.Step);
        Assert.False(wizard.CanGoBack);
        Assert.Equal("Memory", wizard.ConnectionName);
    }

    // ───────────── accounts ─────────────

    [AvaloniaFact]
    public void AccountsListWhoUsesThemAndSigningOutRemovesThem()
    {
        var signIn = _host.Services.GetRequiredService<SourceSignInService>();
        var account = signIn.Accounts.Add("toucan.sources.rest", new Uri("https://strings.example.com/"), "token", "work account");
        signIn.Accounts.Bind("/some/project", "c1", account.Id);
        var vm = new AccountsViewModel(signIn, _host.Services.GetRequiredService<ISourceAdapterCatalog>(), _host.Services.GetRequiredService<IRecentProjectService>(),
            _host.Dialogs, _host.Messages, _host.Services.GetRequiredService<ISourceConnectionService>());

        var row = Assert.Single(vm.Accounts);
        Assert.Equal(("work account", "Toucan source protocol · strings.example.com"), (row.Name, row.Service));
        Assert.Equal("Used by another project", row.UsageText);
        Assert.True(vm.HasAccounts);
        Assert.DoesNotContain(vm.Services, s => s.Descriptor.Id == "toucan.sources.local");

        vm.SignOutCommand.Execute(row);

        Assert.Single(_host.Messages.Confirmations);
        Assert.Empty(vm.Accounts);
        Assert.True(vm.HasNoAccounts);
    }

    [AvaloniaFact]
    public void DecliningTheConfirmationKeepsTheAccount()
    {
        var signIn = _host.Services.GetRequiredService<SourceSignInService>();
        signIn.Accounts.Add("toucan.sources.rest", new Uri("https://strings.example.com/"), "token", "work account");
        var vm = new AccountsViewModel(signIn, _host.Services.GetRequiredService<ISourceAdapterCatalog>(), _host.Services.GetRequiredService<IRecentProjectService>(),
            _host.Dialogs, _host.Messages, null);
        _host.Messages.ConfirmAnswer = false;

        vm.SignOutCommand.Execute(vm.Accounts[0]);

        Assert.Single(vm.Accounts);
    }

    // ───────────── views ─────────────

    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static IEnumerable<T> Visual<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>();

    [AvaloniaFact]
    public void PlanDialogOpensOnConflictsWhenAskedAndEveryToggleHasAName()
    {
        var vm = new SyncPlanViewModel(PlanWith([Pull("a", "x")], [Push("c", "z")], [Conflict()]), "Memory") { StartOnConflicts = true };
        var dialog = new SyncPlanDialog(vm);
        dialog.Show();
        Pump();

        var tabs = Visual<TabControl>(dialog).Single();
        Assert.Equal(2, tabs.SelectedIndex);
        tabs.SelectedIndex = 0;
        Pump();
        var boxes = Visual<CheckBox>(dialog).ToList();
        Assert.NotEmpty(boxes);
        Assert.All(boxes, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        dialog.Close();
    }

    [AvaloniaFact]
    public void PlanDialogClosesWhenTheUserApplies()
    {
        var vm = new SyncPlanViewModel(PlanWith([Pull("a", "x")]), "Memory");
        var dialog = new SyncPlanDialog(vm);
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Show();
        vm.ApplyCommand.Execute(null);
        Pump();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task WizardAndAccountsDialogsRender()
    {
        var vm = await OpenProjectAsync();
        var wizard = new AddConnectionDialog(Wizard(vm));
        wizard.Show();
        Pump();
        Assert.Contains(Visual<ListBox>(wizard), l => l.ItemCount >= 2);
        wizard.Close();

        var signIn = _host.Services.GetRequiredService<SourceSignInService>();
        var accounts = new AccountsDialog(new AccountsViewModel(signIn, _host.Services.GetRequiredService<ISourceAdapterCatalog>(),
            _host.Services.GetRequiredService<IRecentProjectService>(), _host.Dialogs, _host.Messages, null));
        accounts.Show();
        Pump();
        Assert.Contains(Visual<TextBlock>(accounts), t => t.Text == "No accounts yet.");
        accounts.Close();
    }

    [AvaloniaFact]
    public async Task SourcesPanelShowsTheConnectionCardWithItsActions()
    {
        var vm = await OpenProjectAsync();
        await ConnectAsync(vm);
        var panel = new SourcesPanel { DataContext = vm };
        var window = new Window { Content = panel };
        window.Show();
        Pump();

        // Sync is the main button; Pull and Push are icon buttons with names; the rest lives in a menu.
        var names = Visual<Button>(panel).Select(b => AutomationProperties.GetName(b)).Where(n => !string.IsNullOrEmpty(n)).ToList();
        Assert.Contains("Sync", names);
        Assert.Contains("Pull", names);
        Assert.Contains("Push", names);
        Assert.Contains("More", names);
        Assert.Contains(Visual<TextBlock>(panel), t => t.Text == "Memory");
        Assert.Contains(Visual<TextBlock>(panel), t => t.Text == "Ready");
        window.Close();
    }

    // ───────────── accessibility (content-sources task 5.8) ─────────────

    /// <summary>
    /// Controls a keyboard or screen-reader user meets on <paramref name="root"/> that have no announceable name (an explicit
    /// name, a label link, visible text or a tooltip) or cannot be reached with Tab. Parts of a control's own template, such
    /// as scrollbar buttons, are left to that control's automation peer.
    /// </summary>
    private static List<string> AccessibilityGaps(Control root, string screen)
    {
        static bool Named(Control c)
        {
            if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)) || AutomationProperties.GetLabeledBy(c) is not null) return true;
            if (ToolTip.GetTip(c) is string { Length: > 0 }) return true;
            return c is ContentControl { Content: string text } && !string.IsNullOrWhiteSpace(text)
                || c is ContentControl { Content: Control content } && content.GetVisualDescendants().Prepend(content).OfType<TextBlock>().Any(t => !string.IsNullOrWhiteSpace(t.Text));
        }
        var interactive = root.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or global::Avalonia.Controls.Primitives.ToggleButton or TextBox or ComboBox or ListBox)
            .Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
            .Where(c => c.TemplatedParent is not global::Avalonia.Controls.Primitives.TemplatedControl || c.TemplatedParent is ContentControl { Content: not null } && c.TemplatedParent is not ListBoxItem and not ComboBoxItem and not TabItem and not Expander);
        var gaps = new List<string>();
        foreach (var c in interactive)
        {
            if (c.TemplatedParent is global::Avalonia.Controls.Primitives.TemplatedControl && (c.Name?.StartsWith("PART_", StringComparison.Ordinal) ?? false)) continue;
            var label = c.Name ?? (c as ContentControl)?.Content?.ToString() ?? c.GetType().Name;
            if (!Named(c)) gaps.Add($"{screen}: {c.GetType().Name} '{label}' has no accessible name");
            // A list is reached through its items: Tab moves focus to the selected (or first) item.
            var reachable = c is ListBox list
                ? list.GetVisualDescendants().OfType<ListBoxItem>().All(i => i.Focusable) && list.ItemCount > 0 || list.ItemCount == 0
                : c.Focusable && global::Avalonia.Input.KeyboardNavigation.GetIsTabStop(c);
            if (!reachable) gaps.Add($"{screen}: {c.GetType().Name} '{label}' is not reachable with Tab");
        }
        return gaps;
    }

    [AvaloniaFact]
    public async Task EverySourcesScreenIsNamedAndReachableByKeyboard()
    {
        var gaps = new List<string>();

        var vm = await OpenProjectAsync();
        await ConnectAsync(vm);
        var window = new Window { Content = new SourcesPanel { DataContext = vm }, Width = 420, Height = 700 };
        window.Show();
        Pump();
        gaps.AddRange(AccessibilityGaps(window, "Sources panel"));
        window.Close();

        var wizardVm = Wizard(vm);
        var wizard = new AddConnectionDialog(wizardVm);
        wizard.Show();
        foreach (var step in Enum.GetValues<ConnectionStep>())
        {
            wizardVm.Step = step;
            Pump();
            gaps.AddRange(AccessibilityGaps(wizard, $"Add connection ({step})"));
        }
        wizard.Close();

        var signIn = _host.Services.GetRequiredService<SourceSignInService>();
        var accounts = new AccountsDialog(new AccountsViewModel(signIn, _host.Services.GetRequiredService<ISourceAdapterCatalog>(),
            _host.Services.GetRequiredService<IRecentProjectService>(), _host.Dialogs, _host.Messages, null));
        accounts.Show();
        Pump();
        gaps.AddRange(AccessibilityGaps(accounts, "Accounts"));
        accounts.Close();

        var planVm = new SyncPlanViewModel(PlanWith([Pull("a", "x")], [Push("c", "z")], [Conflict()]), "Memory");
        var plan = new SyncPlanDialog(planVm);
        plan.Show();
        var tabs = Visual<TabControl>(plan).Single();
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Pump();
            gaps.AddRange(AccessibilityGaps(plan, $"Sync plan (tab {i + 1})"));
        }
        plan.Close();

        Assert.True(gaps.Count == 0, "Accessibility gaps:\n" + string.Join("\n", gaps.Distinct()));
    }
}
