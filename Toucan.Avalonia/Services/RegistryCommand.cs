using System.Windows.Input;
using Avalonia.Threading;
using Toucan.Core.Commands;

namespace Toucan.Avalonia.Services;

/// <summary>An <see cref="ICommand"/> that runs a registry command, so menus, key bindings and the palette share one path.</summary>
internal sealed class RegistryCommand : ICommand
{
    private readonly ICommandRegistry _registry;
    private readonly string _id;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ICommandRegistry, Dictionary<string, RegistryCommand>> s_cache = [];

    /// <summary>One instance per command, so rebuilding menus and key bindings does not pile up event subscriptions.</summary>
    public static RegistryCommand For(ICommandRegistry registry, string id)
    {
        var table = s_cache.GetOrCreateValue(registry);
        lock (table)
        {
            if (!table.TryGetValue(id, out var command)) table[id] = command = new RegistryCommand(registry, id);
            return command;
        }
    }

    private RegistryCommand(ICommandRegistry registry, string id)
    {
        _registry = registry;
        _id = id;
        // State can change with the project, the connection or the handler's own conditions.
        registry.CommandsChanged += (_, _) => RaiseCanExecuteChanged();
        if (registry.Find(id) is { } command && BuiltInCommands.InnerCommand(command) is { } inner)
            inner.CanExecuteChanged += (_, _) => RaiseCanExecuteChanged();
    }

    // The registry raises its events on whatever thread changed it (closing a project, disposing the container).
    // Avalonia answers CanExecuteChanged by waiting for the UI thread, so raising it from another thread while the UI
    // thread waits for that thread (container disposal) deadlocks. Hand it to the UI thread instead of waiting.
    private void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        else Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _registry.GetState(_id).CanRun;

    public void Execute(object? parameter) => _ = _registry.ExecuteAsync(_id, parameter);
}
