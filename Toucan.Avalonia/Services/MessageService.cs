using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;

namespace Toucan.Avalonia.Services;

public class MessageService : IMessageService
{
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public void ShowMessage(string message, string title = "Info")
    {
        // Fire-and-forget on UI thread — use async version internally
        _ = ShowMessageAsync(message, title);
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        // ponytail: sync wrapper for legacy code paths; async preferred
        return ShowConfirmationAsync(message, title).GetAwaiter().GetResult();
    }

    public async Task ShowMessageAsync(string message, string title = "Info")
    {
        var owner = GetMainWindow();
        var dlg = new MessageWindow(title, message);
        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
    {
        var owner = GetMainWindow();
        if (owner == null) return false;
        var dlg = new ConfirmWindow(title, message);
        var result = await dlg.ShowDialog<bool?>(owner);
        return result == true;
    }
}
