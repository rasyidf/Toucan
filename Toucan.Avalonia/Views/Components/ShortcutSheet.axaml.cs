using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// Overlay listing every keyboard shortcut, grouped by menu (Cmd/Ctrl+/ or Help › Keyboard Shortcuts).
/// Visibility follows <see cref="MainWindowViewModel.IsShortcutSheetOpen"/>; Esc or a click outside closes it.
/// </summary>
public partial class ShortcutSheet : UserControl
{
    public ShortcutSheet()
    {
        InitializeComponent();
        IsVisible = false;
        Scrim.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Scrim)) Close();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || change.NewValue is not true) return;
        Groups.ItemsSource = KeybindingService.GetDefinitions()
            .GroupBy(d => d.Category)
            .Select(g => new ShortcutGroup(Loc.T(g.Key), g.Select(d => d with { Action = Loc.T(d.Action) }).ToList()))
            .ToList();
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    private void Close()
    {
        if (DataContext is MainWindowViewModel vm) vm.IsShortcutSheetOpen = false;
    }
}
