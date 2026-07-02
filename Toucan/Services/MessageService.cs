using System.Windows;
using Toucan.Core.Contracts;
using Wpf.Ui.Controls;

namespace Toucan.Services;

internal class MessageService : IMessageService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowMessage(string message, string title = "Info")
    {
        var msgBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            Owner = Owner,
            PrimaryButtonText = "OK",
            CloseButtonText = string.Empty
        };

        _ = msgBox.ShowDialogAsync();
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var msgBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            Owner = Owner,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No"
        };

        var result = msgBox.ShowDialogAsync().GetAwaiter().GetResult();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}
