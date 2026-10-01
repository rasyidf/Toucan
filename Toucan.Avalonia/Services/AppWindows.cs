using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Helpers for locating the window that should own dialogs, pickers, and clipboard access.
/// Dialogs opened from inside another dialog must be owned by that dialog, not the main window.
/// </summary>
internal static class AppWindows
{
    public static IClassicDesktopStyleApplicationLifetime? Desktop =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public static Window? Main => Desktop?.MainWindow;

    /// <summary>The active window (topmost modal dialog if any), falling back to the main window.</summary>
    public static Window? Active
    {
        get
        {
            var desktop = Desktop;
            if (desktop == null) return null;
            return desktop.Windows.LastOrDefault(w => w.IsActive)
                ?? desktop.Windows.LastOrDefault(w => w.IsVisible)
                ?? desktop.MainWindow;
        }
    }
}
