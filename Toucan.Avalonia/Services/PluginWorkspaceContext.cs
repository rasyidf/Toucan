using System.ComponentModel;
using Avalonia.Threading;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Commands;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.Services;

/// <summary>The narrow view of the open project that plugin views get, instead of <see cref="MainWindowViewModel"/>.</summary>
internal sealed class PluginWorkspaceContext : IPluginWorkspace, IDisposable
{
    private readonly MainWindowViewModel _vm;
    private readonly ICommandRegistry _commands;

    public PluginWorkspaceContext(MainWindowViewModel vm, ICommandRegistry commands)
    {
        _vm = vm;
        _commands = commands;
        _vm.PropertyChanged += OnViewModelChanged;
    }

    public event EventHandler? Changed;

    public bool HasWorkspace => _vm.HasProject;
    public string? WorkspaceId => _vm.HasProject ? _commands.WorkspaceId : null;
    public string? ProjectPath => _vm.HasProject ? _vm.CurrentPath : null;
    public string? SelectedKey => _vm.HasProject && !string.IsNullOrEmpty(_vm.SelectedKeyNamespace) ? _vm.SelectedKeyNamespace : null;

    public async Task<bool> ExecuteCommandAsync(string commandId, object? parameter = null, CancellationToken cancellationToken = default)
    {
        var run = await _commands.ExecuteAsync(commandId, parameter, cancellationToken).ConfigureAwait(false);
        return run.Status == CommandRunStatus.Completed;
    }

    public void Dispose() => _vm.PropertyChanged -= OnViewModelChanged;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainWindowViewModel.HasProject) or nameof(MainWindowViewModel.CurrentPath) or nameof(MainWindowViewModel.SelectedKeyNamespace)))
            return;
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke(this, EventArgs.Empty);
        else Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
