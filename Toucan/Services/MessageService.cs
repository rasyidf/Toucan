using System.Windows;

namespace Toucan.Services;

internal interface IMessageService
{
    void ShowMessage(string message, string title = "Info");
    bool ShowConfirmation(string message, string title = "Confirm");
}

internal class MessageService : IMessageService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowMessage(string message, string title = "Info")
    {
        if (Owner != null) MessageBox.Show(Owner, message, title);
        else MessageBox.Show(message, title);
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var result = Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo);
        return result == MessageBoxResult.Yes;
    }
}
