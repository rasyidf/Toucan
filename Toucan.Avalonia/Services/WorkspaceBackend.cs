using Avalonia.Threading;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Avalonia.Services;

/// <summary>Lets plugins read and change the project the main window shows. The view model is UI-thread bound, so every call hops there.</summary>
internal sealed class WorkspaceBackend : IWorkspaceBackend
{
    private readonly MainWindowViewModel _vm;

    public WorkspaceBackend(MainWindowViewModel vm)
    {
        _vm = vm;
        vm.WorkspaceChanged += (_, e) => Changed?.Invoke(this, e);
    }

    public event EventHandler? Changed;

    public bool IsOpen => _vm.IsWorkspaceOpen;

    public Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default) => OnUi(_vm.BuildWorkspaceSnapshot, cancellationToken);

    public Task<EditResult> ApplyAsync(string pluginId, WorkspaceEdit edit, CancellationToken cancellationToken = default) =>
        OnUi(() => _vm.ApplyWorkspaceEdit(pluginId, edit), cancellationToken);

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
