using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Content sources: the Sources panel, the commands that pull, push and sync, and the delivery badges on rows.</summary>
public partial class MainWindowViewModel
{
    private SyncCoordinator? _syncCoordinator;
    private ISourceConnectionService? _sourceConnections;
    private SourceSignInService? _sourceSignIn;
    private ISourceAdapterCatalog? _sourceAdapters;

    /// <summary>The Sources panel's state; null when the host has no content-source services.</summary>
    public SourcesViewModel? Sources { get; private set; }

    public bool HasSources => Sources is not null;

    private void InitializeSources(MainWindowServices services)
    {
        _syncCoordinator = services.Sync;
        _sourceConnections = services.SourceConnections;
        _sourceSignIn = services.SignIn;
        _sourceAdapters = services.SourceAdapters;
        if (_syncCoordinator is null || _sourceConnections is null || _sourceAdapters is null) return;
        Sources = new SourcesViewModel(_syncCoordinator, _sourceConnections, _sourceAdapters, _dialogService, _messageService, _notifications,
            () => ProjectSettings, CreateAddConnection, CreateAccounts, () => StatusBarService.Instance.ViewModel?.Sync);
        _lifecycleService.ProjectChanged += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Sources.Reload());
        _syncCoordinator.DeliveryChanged += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDeliveryBadges);
    }

    private AddConnectionViewModel? CreateAddConnection()
    {
        if (ProjectSettings is not { } project || _sourceConnections is null || _sourceSignIn is null || _sourceAdapters is null) return null;
        return new AddConnectionViewModel(_sourceSignIn, _sourceConnections, _sourceAdapters, _dialogService, _messageService, project.Sources,
            [.. AllTranslation.Select(t => t.Language).Distinct(StringComparer.OrdinalIgnoreCase)])
        {
            ProjectKeyPaths = () => AllTranslation.Select(t => t.Namespace).Distinct(StringComparer.Ordinal).Select(n => (IReadOnlyList<string>)n.Split('.')),
        };
    }

    private AccountsViewModel? CreateAccounts() => _sourceSignIn is null || _sourceAdapters is null ? null
        : new AccountsViewModel(_sourceSignIn, _sourceAdapters, _recentFileService, _dialogService, _messageService, _sourceConnections);

    partial void OnIsDirtyChanged(bool value) => StatusBarService.Instance.ViewModel?.Sync.UpdateSaved(value);

    /// <summary>Sets the delivery state on a row as it is created.</summary>
    internal void ApplyDelivery(TranslationItemViewModel item)
    {
        if (_syncCoordinator is null || Workspace.GetProjectionEntryId(item.Model) is not { } entry) return;
        item.SetDelivery(_syncCoordinator.DeliveryOf(entry, item.Language));
    }

    private void RefreshDeliveryBadges()
    {
        foreach (var shown in PagingController.Data.SelectMany(g => g.AllItems)) ApplyDelivery(shown);
    }

    [RelayCommand] private void PullSource() => Sources?.PullCommand.Execute(null);
    [RelayCommand] private void PushSource() => Sources?.PushCommand.Execute(null);
    [RelayCommand] private void SyncSource() => Sources?.SyncNowCommand.Execute(null);
    [RelayCommand] private void ShowSourceConflicts() => Sources?.ShowConflictsCommand.Execute(null);
    [RelayCommand] private void AddSourceConnection() => Sources?.AddConnectionCommand.Execute(null);
    [RelayCommand] private void ShowSourceAccounts() => Sources?.ShowAccountsCommand.Execute(null);

    /// <summary>Opens the conflict list for the row's connection (the badge on a row calls this).</summary>
    [RelayCommand] private void OpenRowConflicts(TranslationItemViewModel? item) => Sources?.ShowConflictsCommand.Execute(null);
}
