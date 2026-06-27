using System.Windows;
using Toucan.Core.Contracts;

namespace Toucan.Services;

internal class MessageService : IMessageService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowMessage(string message, string title = "Info")
    {
        if (Owner != null)
        {
            _ = MessageBox.Show(Owner, message, title);
        }
        else
        {
            _ = MessageBox.Show(message, title);
        }
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        MessageBoxResult result = Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo);
        return result == MessageBoxResult.Yes;
    }
}
