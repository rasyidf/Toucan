using System.Windows.Input;

namespace Toucan.Avalonia.Services;

/// <summary>One entry in the command palette: what it is called, where it lives in the menus, and how to run it.</summary>
public sealed record PaletteCommand(string Category, string Title, string? Shortcut, ICommand Command, object? Parameter = null)
{
    public bool CanRun => Command.CanExecute(Parameter);
}
