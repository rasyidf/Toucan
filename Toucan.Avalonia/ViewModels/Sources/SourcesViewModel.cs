using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One connection in the Sources panel.</summary>
public sealed partial class SourceConnectionItemViewModel(SourceConnectionSettings settings) : ObservableObject
{
    public SourceConnectionSettings Settings { get; } = settings;
    public string Id => Settings.Id;
    public string Name => Settings.Name ?? Settings.RemoteProject?.Name ?? Settings.Id;
    public string AdapterName { get; set; } = string.Empty;
    public string AccessibleName => $"{Name}, {StateText}";

    [ObservableProperty] private string stateText = "Not connected";
    [ObservableProperty] private string lastPullText = "Never pulled";
    [ObservableProperty] private string lastPushText = "Never pushed";
    [ObservableProperty] private string pendingText = string.Empty;
    [ObservableProperty] private int conflictCount;
    [ObservableProperty] private int pendingCount;
    [ObservableProperty] private bool needsSignIn;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool remoteChangesAvailable;
    [ObservableProperty] private bool isFailed;

    public bool HasConflicts => ConflictCount > 0;
    public bool CanSync => !IsBusy && !NeedsSignIn;

    partial void OnConflictCountChanged(int value) => OnPropertyChanged(nameof(HasConflicts));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSync));
    partial void OnNeedsSignInChanged(bool value) => OnPropertyChanged(nameof(CanSync));
    partial void OnStateTextChanged(string value) => OnPropertyChanged(nameof(AccessibleName));

    public void Apply(SourceConnectionStatus? connection, SyncStatus? sync)
    {
        NeedsSignIn = connection?.State is SourceConnectionState.SignInRequired or SourceConnectionState.ChooseAccount || sync?.NeedsSignIn == true;
        IsFailed = connection?.State is SourceConnectionState.Failed or SourceConnectionState.AdapterUnavailable or SourceConnectionState.Unbound;
        IsBusy = sync is { State: not SyncRunState.Idle };
        ConflictCount = sync?.Conflicts ?? 0;
        PendingCount = sync?.PendingChanges ?? 0;
        RemoteChangesAvailable = sync?.RemoteChangesAvailable ?? false;
        PendingText = PendingCount > 0 ? $"{PendingCount} not synced" : string.Empty;
        LastPullText = sync?.LastPull is { } pull ? $"Pulled {pull.LocalDateTime:g}" : "Never pulled";
        LastPushText = sync?.LastPush is { } push ? $"Pushed {push.LocalDateTime:g}" : "Never pushed";
        StateText = sync is { State: not SyncRunState.Idle } ? sync.State switch
            {
                SyncRunState.Planning => "Reading…",
                SyncRunState.Pulling => "Pulling…",
                _ => "Sending…",
            }
            : NeedsSignIn ? "Not signed in"
            : IsFailed ? "Failed"
            : ConflictCount > 0 ? $"Conflicts {ConflictCount}"
            : sync?.PartialFailure == true ? "Partly failed"
            : connection?.State == SourceConnectionState.Connected ? "Connected"
            : connection?.State == SourceConnectionState.Connecting ? "Connecting…"
            : "Ready";
    }
}

/// <summary>
/// The Sources panel: the project's connections with their state, last pull and push and pending counts, and the actions on
/// them. Pull, push and sync show the plan first; nothing changes until the user applies it.
/// </summary>
public sealed partial class SourcesViewModel : ObservableObject, IDisposable
{
    private readonly SyncCoordinator _sync;
    private readonly ISourceConnectionService _connections;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly IDialogService _dialogs;
    private readonly IAsyncMessageService _messages;
    private readonly INotificationCenter? _notifications;
    private readonly Func<ProjectSettings?> _project;
    private readonly Func<AddConnectionViewModel?> _addConnection;
    private readonly Func<AccountsViewModel?> _accounts;
    private readonly Func<SyncIndicators?> _indicators;

    public SourcesViewModel(SyncCoordinator sync, ISourceConnectionService connections, ISourceAdapterCatalog adapters, IDialogService dialogs,
        IAsyncMessageService messages, INotificationCenter? notifications, Func<ProjectSettings?> project, Func<AddConnectionViewModel?> addConnection,
        Func<AccountsViewModel?> accounts, Func<SyncIndicators?> indicators)
    {
        (_sync, _connections, _adapters, _dialogs, _messages, _notifications, _project, _addConnection, _accounts, _indicators) =
            (sync, connections, adapters, dialogs, messages, notifications, project, addConnection, accounts, indicators);
        _sync.StatusChanged += OnSyncStatus;
        _connections.StatusChanged += OnConnectionStatus;
        Reload();
    }

    public ObservableCollection<SourceConnectionItemViewModel> Connections { get; } = [];

    [ObservableProperty] private SourceConnectionItemViewModel? selectedConnection;
    [ObservableProperty] private string summary = "No connections";

    public bool HasConnections => Connections.Count > 0;
    public bool HasNoConnections => Connections.Count == 0;
    public bool HasConflicts => Connections.Any(c => c.HasConflicts);

    /// <summary>Rebuilds the list from the open project.</summary>
    public void Reload()
    {
        Post(() =>
        {
            var selected = SelectedConnection?.Id;
            Connections.Clear();
            foreach (var settings in _project()?.Sources.Where(s => !string.IsNullOrWhiteSpace(s.Adapter)) ?? [])
            {
                var item = new SourceConnectionItemViewModel(settings) { AdapterName = _adapters.Find(settings.Adapter ?? string.Empty)?.Descriptor.DisplayName ?? settings.Adapter ?? string.Empty };
                item.Apply(_connections.Status(settings.Id), _sync.Status(settings.Id));
                Connections.Add(item);
            }
            SelectedConnection = Connections.FirstOrDefault(c => c.Id == selected) ?? Connections.FirstOrDefault();
            Refresh();
        });
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(HasNoConnections));
        OnPropertyChanged(nameof(HasConflicts));
        var statuses = _sync.Statuses;
        _indicators()?.UpdateSynced(statuses);
        Summary = Connections.Count == 0 ? "No connections"
            : HasConflicts ? $"{Connections.Sum(c => c.ConflictCount)} conflicts to resolve"
            : Connections.Sum(c => c.PendingCount) is > 0 and var pending ? $"{pending} changes not synced"
            : "Everything is synced";
    }

    private void OnSyncStatus(object? sender, SyncStatusChangedEventArgs e) => Post(() =>
    {
        var item = Connections.FirstOrDefault(c => c.Id == e.Status.ConnectionId);
        item?.Apply(_connections.Status(e.Status.ConnectionId), e.Status);
        Refresh();
        Notify(e.Status);
    });

    private void OnConnectionStatus(object? sender, SourceConnectionStatusChangedEventArgs e) => Post(() =>
    {
        Connections.FirstOrDefault(c => c.Id == e.Status.ConnectionId)?.Apply(e.Status, _sync.Status(e.Status.ConnectionId));
        Refresh();
    });

    // ───────────── notifications ─────────────

    private readonly HashSet<string> _signInShown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _remoteShown = new(StringComparer.Ordinal);

    private void Notify(SyncStatus status)
    {
        if (_notifications is null) return;
        if (status.NeedsSignIn)
        {
            if (_signInShown.Add(status.ConnectionId))
                Publish(new() { Title = $"Sign in to {status.Name}", Message = "Your sign-in expired. Sign in again to keep syncing.", Severity = NotificationSeverity.Warning, Sticky = true, ActionCommandId = "toucan.sources.accounts", ActionLabel = "Sign in" });
        }
        else _signInShown.Remove(status.ConnectionId);
        if (status is { State: SyncRunState.Idle, PartialFailure: true } && !status.NeedsSignIn)
            Publish(new() { Title = $"Some changes were not sent to {status.Name}", Message = status.Message, Severity = NotificationSeverity.Error, ActionCommandId = "toucan.sources.show-conflicts", ActionLabel = "Review" });
        var waiting = status is { State: SyncRunState.Idle, RemoteChangesAvailable: true, Conflicts: 0 };
        if (waiting && !_remoteShown.GetValueOrDefault(status.ConnectionId))
            Publish(new() { Title = $"{status.Name} has new changes", Message = "Pull to bring them in.", Severity = NotificationSeverity.Info, ActionCommandId = "toucan.sources.pull", ActionLabel = "Pull" });
        _remoteShown[status.ConnectionId] = waiting;
    }

    private void Publish(PluginNotification notification) => _notifications?.Publish("toucan", notification);

    // ───────────── commands ─────────────

    [RelayCommand] private Task Pull(SourceConnectionItemViewModel? item) => RunAsync(item, SyncPlanKind.Pull);
    [RelayCommand] private Task Push(SourceConnectionItemViewModel? item) => RunAsync(item, SyncPlanKind.Push);
    [RelayCommand] private Task SyncNow(SourceConnectionItemViewModel? item) => RunAsync(item, SyncPlanKind.Sync);

    /// <summary>The active connection for commands and shortcuts: the selected one, else the only one.</summary>
    public SourceConnectionItemViewModel? Active => SelectedConnection ?? (Connections.Count == 1 ? Connections[0] : null);

    private async Task RunAsync(SourceConnectionItemViewModel? item, SyncPlanKind kind)
    {
        item ??= Active;
        if (item is null) { await _messages.ShowMessageAsync("Add a connection first.", "Sources"); return; }
        if (!item.CanSync) return;
        try
        {
            var plan = await _sync.PlanAsync(item.Id, kind);
            if (!plan.HasChanges && plan.Notices.Count == 0)
            {
                // Agreement is still recorded, so the next plan starts from it and rows show as synced.
                if (plan.HasBookkeeping) await _sync.ApplyAsync(plan);
                await _messages.ShowMessageAsync($"{item.Name} is up to date.", "Sources");
                return;
            }
            var vm = new SyncPlanViewModel(plan, item.Name);
            if (!await _dialogs.ShowSyncPlanAsync(vm)) return;
            var result = await _sync.ApplyAsync(vm.BuildPlan());
            if (!result.Succeeded && result.Message is { } problem) await _messages.ShowMessageAsync(problem + " Nothing is undone at the service; failed changes stay pending.", item.Name);
        }
        catch (SourceException ex)
        {
            await _messages.ShowMessageAsync(ex.Error.Message, item.Name);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException ex)
        {
            await _messages.ShowMessageAsync(ex.Message, item.Name);
        }
    }

    [RelayCommand]
    private async Task ShowConflicts(SourceConnectionItemViewModel? item)
    {
        item ??= Connections.FirstOrDefault(c => c.HasConflicts) ?? Active;
        if (item is null || _sync.LastPlan(item.Id) is not { Conflicts.Count: > 0 } plan) { await _messages.ShowMessageAsync("There are no conflicts.", "Sources"); return; }
        var vm = new SyncPlanViewModel(plan, item.Name) { StartOnConflicts = true };
        if (await _dialogs.ShowSyncPlanAsync(vm)) await _sync.ApplyAsync(vm.BuildPlan());
    }

    [RelayCommand]
    private async Task Test(SourceConnectionItemViewModel? item)
    {
        item ??= Active;
        if (item is null) return;
        var result = await _connections.TestAsync(item.Id);
        await _messages.ShowMessageAsync(result.Succeeded ? result.Message : "Could not connect: " + result.Message, item.Name);
    }

    [RelayCommand]
    private async Task AddConnection()
    {
        if (_addConnection() is not { } wizard) { await _messages.ShowMessageAsync("Open a project first.", "Sources"); return; }
        if (await _dialogs.ShowAddConnectionAsync(wizard) is { } connection) Reload();
    }

    [RelayCommand]
    private async Task Edit(SourceConnectionItemViewModel? item)
    {
        item ??= Active;
        if (item is null || _addConnection() is not { } wizard) return;
        wizard.Edit(item.Settings);
        if (await _dialogs.ShowAddConnectionAsync(wizard) is not null) Reload();
    }

    [RelayCommand]
    private async Task Remove(SourceConnectionItemViewModel? item)
    {
        item ??= Active;
        if (item is null) return;
        if (!await _messages.ConfirmAsync($"Remove {item.Name} from this project? Its sync state on this machine is deleted; the service keeps its content.", "Remove connection", "Remove", "Keep")) return;
        await _connections.RemoveAsync(item.Id);
        if (_project()?.ProjectPath is { } root) new SyncStateStore(root, item.Id).Delete();
        Reload();
    }

    [RelayCommand]
    private async Task ShowAccounts()
    {
        if (_accounts() is { } accounts) await _dialogs.ShowAccountsAsync(accounts);
    }

    public void Dispose()
    {
        _sync.StatusChanged -= OnSyncStatus;
        _connections.StatusChanged -= OnConnectionStatus;
    }

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
