using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Commands;

namespace Toucan.Avalonia.Services;

/// <summary>Describes a keybinding for display in the Options dialog.</summary>
public sealed record KeybindingEntry(string Category, string Action, string Shortcut, string? CommandId = null);

/// <summary>
/// Turns the shortcuts of the command registry into window key bindings and display text. "Mod" in a shortcut means
/// Cmd on macOS and Ctrl elsewhere, so the same definitions produce native-feeling shortcuts on every platform.
/// </summary>
internal static class KeybindingService
{
    private static ICommandRegistry? s_registry;
    private static WeakReference<MainWindowViewModel>? s_boundVm;

    /// <summary>The registry in use. Created on first use when the host did not supply one (tests).</summary>
    public static ICommandRegistry Registry
    {
        get
        {
            if (s_registry is null)
            {
                s_registry = new CommandRegistry();
                BuiltInCommands.Bind(s_registry, null);
            }
            return s_registry;
        }
    }

    /// <summary>Uses the application's registry; call before the main window is created.</summary>
    public static void UseRegistry(ICommandRegistry registry)
    {
        s_registry = registry;
        s_boundVm = null;
    }

    /// <summary>Converts portable shortcut text (<c>Mod+Shift+K</c>) to a gesture; null when a key name is unknown.</summary>
    public static KeyGesture? ToGesture(string? shortcut)
    {
        if (!ShortcutText.TryNormalize(shortcut, out var normalized)) return null;
        var parts = normalized.Split('+');
        var modifiers = KeyModifiers.None;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            modifiers |= part switch
            {
                "Mod" => PlatformService.IsMacOS ? KeyModifiers.Meta : KeyModifiers.Control,
                "Ctrl" => KeyModifiers.Control,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                _ => KeyModifiers.Meta,
            };
        }
        return Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) && key != Key.None ? new KeyGesture(key, modifiers) : null;
    }

    private static string Display(KeyGesture gesture) => gesture.ToString("p", null);

    /// <summary>Shortcuts that are bound, for the shortcut sheet and settings.</summary>
    public static List<KeybindingEntry> GetDefinitions()
    {
        var registry = Registry;
        var list = new List<KeybindingEntry>();
        foreach (var command in registry.Commands)
        {
            if (ToGesture(registry.GetShortcut(command.Id)) is not { } gesture) continue;
            list.Add(new KeybindingEntry(registry.GetCategory(command.Id), registry.GetTitle(command.Id), Display(gesture), command.Id));
        }
        list.Add(new KeybindingEntry("View", "Zen / Focused: next item", "J or ↓"));
        list.Add(new KeybindingEntry("View", "Zen / Focused: previous item", "K or ↑"));
        return list;
    }

    /// <summary>Gesture for a built-in action name ("Save") or a command ID, for menu item labels.</summary>
    public static KeyGesture? GestureFor(string actionOrId)
    {
        var id = Registry.Find(actionOrId) is not null ? actionOrId : BuiltInCommands.FindByAction(actionOrId)?.Id;
        return id is null ? null : ToGesture(Registry.GetShortcut(id));
    }

    /// <summary>True for shortcuts that must keep their text-editing meaning inside a text box.</summary>
    public static bool IsTextEditingKey(string action) => BuiltInCommands.FindByAction(action)?.SkipInTextBox == true;

    /// <param name="handledByMenu">Gestures already owned by the native (macOS) menu; binding them twice would fire twice.</param>
    public static void Apply(Window window, MainWindowViewModel vm, IReadOnlySet<KeyGesture>? handledByMenu = null)
    {
        var registry = Registry;
        if (!(s_boundVm?.TryGetTarget(out var bound) == true && ReferenceEquals(bound, vm)))
        {
            BuiltInCommands.Bind(registry, vm);
            registry.LoadCustomShortcuts(vm.AppOptions.CustomShortcuts);
            s_boundVm = new WeakReference<MainWindowViewModel>(vm);
        }
        Refresh(window, handledByMenu);
    }

    /// <summary>Rebuilds the window's key bindings from the registry's current shortcuts. Raises no registry events.</summary>
    public static void Refresh(Window window, IReadOnlySet<KeyGesture>? handledByMenu = null)
    {
        var registry = Registry;
        window.KeyBindings.Clear();
        foreach (var command in registry.Commands)
        {
            if (ToGesture(registry.GetShortcut(command.Id)) is not { } gesture) continue;
            if (handledByMenu?.Contains(gesture) == true) continue;
            ICommand registryCommand = RegistryCommand.For(registry, command.Id);
            var builtIn = BuiltInCommands.All.FirstOrDefault(b => b.Id == command.Id);
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = gesture,
                Command = builtIn?.SkipInTextBox == true ? new TextBoxGuardCommand(window, registryCommand) : registryCommand,
            });
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
