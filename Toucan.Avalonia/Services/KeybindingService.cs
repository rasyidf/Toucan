using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Services;

/// <summary>Describes a keybinding for display in the Options dialog.</summary>
public sealed record KeybindingEntry(string Category, string Action, string Shortcut);

/// <summary>
/// Single source of truth for keyboard shortcuts. "Primary" means Cmd on macOS and Ctrl elsewhere,
/// so the same table produces native-feeling shortcuts on every platform.
/// </summary>
internal static class KeybindingService
{
    private sealed record Binding(string Category, string Action, Key Key, KeyModifiers Modifiers, Func<MainWindowViewModel, ICommand> Command, bool SkipInTextBox = false, object? Parameter = null);

    private static KeyModifiers Primary => PlatformService.IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control;

    private static List<Binding> Table =>
    [
        new("File", "New Project", Key.N, Primary | KeyModifiers.Shift, vm => vm.NewFolderCommand),
        new("File", "Open Folder", Key.O, Primary, vm => vm.OpenFolderCommand),
        new("File", "Open Recent", Key.R, Primary, vm => vm.OpenRecentCommand),
        new("File", "Save", Key.S, Primary, vm => vm.SaveCommand),
        new("File", "Save As", Key.S, Primary | KeyModifiers.Shift, vm => vm.SaveToCommand),
        new("File", "Close Project", Key.W, Primary, vm => vm.CloseProjectCommand),
        new("File", "Refresh", Key.F5, KeyModifiers.None, vm => vm.RefreshCommand),
        new("Edit", "Undo", Key.Z, Primary, vm => vm.UndoCommand, SkipInTextBox: true),
        new("Edit", "Redo", PlatformService.IsMacOS ? Key.Z : Key.Y, PlatformService.IsMacOS ? Primary | KeyModifiers.Shift : Primary, vm => vm.RedoCommand, SkipInTextBox: true),
        new("Edit", "Add Translation Key", Key.I, Primary, vm => vm.NewItemCommand),
        new("Edit", "Add Language", Key.L, Primary, vm => vm.NewLanguageCommand),
        new("Edit", "Rename", Key.F2, KeyModifiers.None, vm => vm.RenameItemCommand, SkipInTextBox: true),
        new("Edit", "Delete", Key.Delete, KeyModifiers.None, vm => vm.DeleteItemCommand, SkipInTextBox: true),
        new("Edit", "Duplicate", Key.D, Primary, vm => vm.DuplicateItemCommand),
        new("Edit", "Copy Template 1", Key.D1, Primary, vm => vm.CopyAsTemplateCommand, Parameter: 0),
        new("Edit", "Copy Template 2", Key.D2, Primary, vm => vm.CopyAsTemplateCommand, Parameter: 1),
        new("Edit", "Copy Template 3", Key.D3, Primary, vm => vm.CopyAsTemplateCommand, Parameter: 2),
        new("Edit", "Copy Template 4", Key.D4, Primary, vm => vm.CopyAsTemplateCommand, Parameter: 3),
        new("Edit", "Copy Template 5", Key.D5, Primary, vm => vm.CopyAsTemplateCommand, Parameter: 4),
        new("Find", "Find", Key.F, Primary, vm => vm.FocusSearchCommand),
        new("Find", "Search & Replace", Key.F, Primary | KeyModifiers.Shift, vm => vm.OpenSearchPanelCommand),
        new("Find", "Next Page", Key.F3, KeyModifiers.None, vm => vm.NextPageCommand),
        new("Find", "Clear Filter", Key.Escape, KeyModifiers.None, vm => vm.ClearFilterCommand, SkipInTextBox: true),
        new("View", "Command Palette", Key.P, Primary | KeyModifiers.Shift, vm => vm.ToggleCommandPaletteCommand),
        new("View", "Keyboard Shortcuts", Key.OemQuestion, Primary, vm => vm.ToggleShortcutSheetCommand),
        new("View", "Toggle Left Panel", Key.B, Primary, _ => PanelService.Instance.ToggleSidebarCommand),
        new("View", "Toggle Right Panel", Key.B, Primary | KeyModifiers.Alt, _ => PanelService.Instance.ToggleInspectorCommand),
        new("View", "Focused Editor", Key.E, Primary, vm => vm.ToggleFocusedEditorCommand),
        new("View", "Zen Mode", Key.Enter, Primary | KeyModifiers.Shift, vm => vm.ToggleZenModeCommand),
        new("View", "Fullscreen", PlatformService.IsMacOS ? Key.F : Key.F11, PlatformService.IsMacOS ? Primary | KeyModifiers.Control : KeyModifiers.None, vm => vm.ToggleFullscreenCommand),
        new("View", "Editor Mode", Key.D1, Primary | KeyModifiers.Alt, vm => vm.SwitchToEditorModeCommand),
        new("View", "Review Mode", Key.D2, Primary | KeyModifiers.Alt, vm => vm.SwitchToReviewModeCommand),
        new("View", "Audit Mode", Key.D3, Primary | KeyModifiers.Alt, vm => vm.SwitchToAuditModeCommand),
        new("Translate", "Pre-translate", Key.T, Primary | KeyModifiers.Shift, vm => vm.PreTranslateBulkCommand),
        new("Translate", "Run Validation", Key.F7, KeyModifiers.None, vm => vm.RunValidationCommand),
        new("Settings", "Preferences", Key.OemComma, Primary, vm => vm.ShowPreferencesCommand),
    ];

    public static List<KeybindingEntry> GetDefinitions()
    {
        var list = Table.Select(b => new KeybindingEntry(b.Category, b.Action, new KeyGesture(b.Key, b.Modifiers).ToString("p", null))).ToList();
        list.Add(new KeybindingEntry("View", "Zen / Focused: next item", "J or ↓"));
        list.Add(new KeybindingEntry("View", "Zen / Focused: previous item", "K or ↑"));
        return list;
    }

    /// <summary>Returns the platform gesture for an action, for menu item labels.</summary>
    public static KeyGesture? GestureFor(string action)
    {
        var b = Table.FirstOrDefault(x => x.Action == action);
        return b == null ? null : new KeyGesture(b.Key, b.Modifiers);
    }

    /// <summary>True for shortcuts that must keep their text-editing meaning inside a text box.</summary>
    public static bool IsTextEditingKey(string action) => Table.FirstOrDefault(x => x.Action == action)?.SkipInTextBox == true;

    /// <param name="handledByMenu">Gestures already owned by the native (macOS) menu; binding them twice would fire twice.</param>
    public static void Apply(Window window, MainWindowViewModel vm, IReadOnlySet<KeyGesture>? handledByMenu = null)
    {
        window.KeyBindings.Clear();
        foreach (var b in Table)
        {
            var gesture = new KeyGesture(b.Key, b.Modifiers);
            if (handledByMenu?.Contains(gesture) == true) continue;
            var command = b.Command(vm);
            var binding = new KeyBinding
            {
                Gesture = gesture,
                Command = b.SkipInTextBox ? new TextBoxGuardCommand(window, command) : command
            };
            if (b.Parameter != null) binding.CommandParameter = b.Parameter;
            window.KeyBindings.Add(binding);
        }
    }

    /// <summary>Bare-key navigation (J/K, arrows) in Zen and Focused modes, when no text box has focus.</summary>
    public static void HandleZenKeys(Window window, KeyEventArgs e, MainWindowViewModel vm)
    {
        // The command palette owns Escape while it is open, whatever has focus.
        if (vm.IsCommandPaletteOpen && e.Key == Key.Escape)
        {
            vm.IsCommandPaletteOpen = false;
            e.Handled = true;
            return;
        }

        if (vm.ZenMode && e.Key == Key.Escape)
        {
            vm.ToggleZenModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((!vm.ZenMode && !vm.FocusedEditorMode) || e.KeyModifiers != KeyModifiers.None) return;
        if (window.FocusManager?.GetFocusedElement() is TextBox) return;

        if (e.Key is Key.J or Key.Down)
        {
            vm.FocusedNextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is Key.K or Key.Up)
        {
            vm.FocusedPreviousCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Runs the inner command only when focus is not in a text box, so Delete/F2/Escape/undo
    /// keep their text-editing meaning while typing a translation.
    /// </summary>
    private sealed class TextBoxGuardCommand(Window window, ICommand inner) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add => inner.CanExecuteChanged += value;
            remove => inner.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter) => inner.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            if (window.FocusManager?.GetFocusedElement() is TextBox) return;
            inner.Execute(parameter);
        }
    }
}
