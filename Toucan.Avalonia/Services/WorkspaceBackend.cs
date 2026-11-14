using Avalonia.Threading;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Avalonia.Services;

/// <summary>Flushes pending desktop text and refreshes projections around the UI-independent Core workspace API.</summary>
internal sealed class WorkspaceBackend : IWorkspaceSubscriptionBackend
{
    private readonly MainWindowViewModel _vm;

    public WorkspaceBackend(MainWindowViewModel vm)
    {
        _vm = vm;
        vm.Workspace.Changed += (_, e) => Changed?.Invoke(this, e);
    }

    public event EventHandler? Changed;

    public bool IsOpen => _vm.IsWorkspaceOpen;

    public Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default) => OnUi(_vm.BuildWorkspaceSnapshot, cancellationToken);

    public Task<EditResult> ApplyAsync(string pluginId, WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        var session = _vm.Workspace.SessionId;
        return OnUi(() => _vm.ApplyWorkspaceEdit(pluginId, edit, session, cancellationToken), cancellationToken);
    }

    public Task<IdentifiedWorkspaceSnapshot?> SnapshotEntriesAsync(CancellationToken cancellationToken = default) =>
        OnUi(() =>
        {
            _vm.FlushPendingEdits();
            return _vm.Workspace.SnapshotEntriesAsync(cancellationToken).GetAwaiter().GetResult();
        }, cancellationToken);

    public Task<EditResult> ApplyEntriesAsync(string pluginId, EntryEdit edit, CancellationToken cancellationToken = default) =>
        OnUi(() =>
        {
            _vm.FlushPendingEdits();
            var result = _vm.Workspace.ApplyEntries(pluginId, edit, cancellationToken);
            if (result.Applied && result.ChangedUnits > 0)
                _vm.RefreshWorkspaceProjection(edit.Operations.Any(op => op is RenameEntry or DeleteEntry), edit.Label ?? pluginId, result.ChangedUnits);
            return result;
        }, cancellationToken);

    public async Task<IWorkspaceSubscription?> SubscribeAsync(int capacity = 128, CancellationToken cancellationToken = default)
    {
        await OnUi(() => { _vm.FlushPendingEdits(); return true; }, cancellationToken).ConfigureAwait(false);
        return await _vm.Workspace.SubscribeAsync(capacity, cancellationToken).ConfigureAwait(false);
    }

    private static Task<T> OnUi<T>(Func<T> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess()) return Task.FromResult(work());
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The caller may have given up while the UI thread was busy; do not change the project for it then.
            cancellationToken.ThrowIfCancellationRequested();
            return work();
        }).GetTask();
    }
}
