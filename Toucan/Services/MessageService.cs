using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Toucan.Services;

internal interface IMessageService
{
    void ShowMessage(string message, string title = "Info");
    bool ShowConfirmation(string message, string title = "Confirm");
}

internal class MessageService : IMessageService
{
    public void ShowMessage(string message, string title = "Info")
    {
        MessageBox.Show(Application.Current.MainWindow, message, title);
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var result = MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo);
        return result == MessageBoxResult.Yes;
    }
}
