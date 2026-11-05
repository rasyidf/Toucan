using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
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
        // A project that was just opened shows its connections without waiting for the panel to be toggled.
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProjectSettings)) Sources?.Reload(); };
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

    /// <summary>
    /// Opens a project that lives at a service: asks which, makes a local folder for it, pulls its files and opens it, listed in the
    /// recent projects as <c>service:name</c>.
    /// </summary>
    [RelayCommand]
    private async Task OpenFromService()
    {
        if (_sourceSignIn is null || _sourceAdapters is null)
        {
            await _messageService.ShowMessageAsync("This build has no content-source services.", "Open from a service");
            return;
        }
        var signIn = _sourceSignIn;
        var catalog = _sourceAdapters;
        var vm = new OpenFromServiceViewModel(signIn, catalog, _dialogService, _messageService)
        {
            Create = async (adapter, account, project, folder, progress, ct) =>
            {
                var full = Path.GetFullPath(folder);
                // The account chosen here is the one the project will use from now on.
                signIn.Accounts.Bind(full, RemoteProjectBootstrap.ConnectionIdFor(adapter), account.Id);
                await using var connections = new SourceConnectionService(null, catalog, signIn);
                var bootstrap = new RemoteProjectBootstrap(async (settings, root, token) =>
                {
                    await connections.OpenWorkspaceAsync(root, [settings], token).ConfigureAwait(false);
                    return await connections.GetSessionAsync(settings.Id, token).ConfigureAwait(false);
                }, (settings, root) => _projectService.DetectFramework(settings, root));
                var address = adapter.Descriptor.DefaultBaseUrl is { } known && SourceAccounts.SameInstance(known, account.BaseUrl) ? null : account.BaseUrl;
                return await bootstrap.CreateAsync(adapter, address, project, full, progress, ct).ConfigureAwait(false);
            },
        };
        if (await _dialogService.ShowOpenFromServiceAsync(vm) is not { } result) return;
        await OpenProjectAsync(result.Folder);
        _recentFileService.SetLabel(result.Folder, result.Label);
        RefreshRecentProjects();
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
