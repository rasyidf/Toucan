using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Toucan.ViewModels;

namespace Toucan.Services;

/// <summary>Describes a keybinding for display in the Options dialog.</summary>
internal record KeybindingEntry(string Category, string Action, string Shortcut);

/// <summary>
/// Centralizes all keyboard shortcuts in one place.
/// Call <see cref="Apply"/> in Window constructor after DataContext is set.
/// ponytail: single source of truth for shortcuts — no more XAML scatter.
/// </summary>
internal static class KeybindingService
{
    /// <summary>Returns all keybinding definitions for UI display.</summary>
    public static List<KeybindingEntry> GetDefinitions() =>
    [
        new("Edit", "Undo", "Ctrl+Z"),
        new("Edit", "Redo", "Ctrl+Y"),
        new("File", "New Project", "Ctrl+Shift+N"),
        new("File", "Open Folder", "Ctrl+O"),
        new("File", "Open Recent", "Ctrl+R"),
        new("File", "Save", "Ctrl+S"),
        new("File", "Save As", "F12"),
        new("File", "Close Project", "Ctrl+F4"),
        new("File", "Exit", "Alt+F4"),
        new("Edit", "Add ID", "Ctrl+I"),
        new("Edit", "Add Language", "Ctrl+L"),
        new("Edit", "Rename", "F2"),
        new("Edit", "Delete", "Delete"),
        new("Edit", "Duplicate", "Ctrl+D"),
        new("Find", "Find", "Ctrl+F"),
        new("Find", "Find Next", "F3"),
        new("Find", "Clear Filter", "Escape"),
        new("Edit", "Copy Template 1", "Ctrl+1"),
        new("Edit", "Copy Template 2", "Ctrl+2"),
        new("Edit", "Copy Template 3", "Ctrl+3"),
        new("View", "Focused Editor", "Ctrl+E"),
        new("View", "Zen Mode", "F11"),
        new("View", "Zen Next (j)", "J"),
        new("View", "Zen Previous (k)", "K"),
    ];

    public static void Apply(Window window, MainWindowViewModel vm)
    {
        var bindings = window.InputBindings;
        bindings.Clear();

        // Edit
        Bind(bindings, Key.Z, ModifierKeys.Control, vm.UndoCommand);
        Bind(bindings, Key.Y, ModifierKeys.Control, vm.RedoCommand);

        // File
        Bind(bindings, Key.N, ModifierKeys.Control | ModifierKeys.Shift, vm.NewFolderCommand);
        Bind(bindings, Key.O, ModifierKeys.Control, vm.OpenFolderCommand);
        Bind(bindings, Key.R, ModifierKeys.Control, vm.OpenRecentCommand);
        Bind(bindings, Key.S, ModifierKeys.Control, vm.SaveCommand);
        Bind(bindings, Key.F12, ModifierKeys.None, vm.SaveToCommand);
        Bind(bindings, Key.F4, ModifierKeys.Control, vm.CloseProjectCommand);
        Bind(bindings, Key.F4, ModifierKeys.Alt, vm.ExitCommand);

        // Edit items
        Bind(bindings, Key.I, ModifierKeys.Control, vm.NewItemCommand);
        Bind(bindings, Key.L, ModifierKeys.Control, vm.NewLanguageCommand);
        Bind(bindings, Key.F2, ModifierKeys.None, vm.RenameItemCommand);
        Bind(bindings, Key.Delete, ModifierKeys.None, vm.DeleteItemCommand);
        Bind(bindings, Key.D, ModifierKeys.Control, vm.DuplicateItemCommand);

        // Find / Filter
        Bind(bindings, Key.F, ModifierKeys.Control, vm.FindPromptCommand);
        Bind(bindings, Key.F3, ModifierKeys.None, vm.FindNextCommand);
        Bind(bindings, Key.Escape, ModifierKeys.None, vm.ClearFilterCommand);

        // Copy templates
        Bind(bindings, Key.D1, ModifierKeys.Control, vm.CopyAsTemplate1Command);
        Bind(bindings, Key.D2, ModifierKeys.Control, vm.CopyAsTemplate2Command);
        Bind(bindings, Key.D3, ModifierKeys.Control, vm.CopyAsTemplate3Command);

        // Editor modes
        Bind(bindings, Key.E, ModifierKeys.Control, vm.ToggleFocusedEditorCommand);
        Bind(bindings, Key.F11, ModifierKeys.None, vm.ToggleZenModeCommand);
        // ponytail: J/K without modifiers can't use KeyGesture — handled via PreviewKeyDown in Window
    }

    /// <summary>Call from Window.PreviewKeyDown to handle bare-key Zen navigation.</summary>
    public static void HandleZenKeys(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (!vm.ZenMode && !vm.FocusedEditorMode) return;
        // Don't intercept if a TextBox has focus
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        if (e.Key == Key.J) { vm.ZenNextCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.K) { vm.ZenPreviousCommand.Execute(null); e.Handled = true; }
    }

    private static void Bind(InputBindingCollection bindings, Key key, ModifierKeys modifiers, ICommand command)
    {
        if (command == null) return;
        bindings.Add(new KeyBinding(command, key, modifiers));
    }
}
