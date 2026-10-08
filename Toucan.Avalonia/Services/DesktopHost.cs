using System.Globalization;
using Avalonia.Controls;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Plugins;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.Services;

/// <summary>Application services handed to plugin views.</summary>
internal sealed class DesktopHost(DesktopContributions contributions, IPluginWorkspace workspace) : IDesktopHost
{
    public CultureInfo Culture => CultureInfo.CurrentUICulture;

    public async Task<bool> ShowDialogAsync(string dialogId, object? parameter = null)
    {
        if (contributions.FindDialog(dialogId) is not { } entry) return false;

        var title = LocalizedText.Pick(entry.Contribution.LocalizedTitles, entry.Contribution.Title, Culture);
        var window = new PluginDialogWindow(title, entry.Contribution.CreateContent(workspace, parameter), entry.Contribution.Width, entry.Contribution.Height);
        if (AppWindows.Active is { } owner) await window.ShowDialog<bool?>(owner);
        else window.Show();
        return true;
    }
}
