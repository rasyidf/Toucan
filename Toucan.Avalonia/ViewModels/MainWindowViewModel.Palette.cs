using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>True while the command palette overlay is showing.</summary>
    [ObservableProperty] private bool isCommandPaletteOpen;


    [RelayCommand]
    private void ToggleCommandPalette() => IsCommandPaletteOpen = !IsCommandPaletteOpen;

    /// <summary>True while the keyboard shortcut sheet is showing.</summary>
    [ObservableProperty] private bool isShortcutSheetOpen;

    [RelayCommand]
    private void ToggleShortcutSheet() => IsShortcutSheetOpen = !IsShortcutSheetOpen;
}
