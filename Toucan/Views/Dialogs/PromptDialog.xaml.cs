using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Toucan;

public partial class PromptDialog : FluentWindow
{
    public PromptDialog(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        titleBarPrompt.Title = title;
        messageLabel.Text = message;
        ResponseTextBox.Text = defaultValue;
        ResponseTextBox.Focus();
        ResponseTextBox.SelectAll();

        // Enter to confirm, Escape to cancel
        var confirmCommand = new RoutedCommand();
        confirmCommand.InputGestures.Add(new KeyGesture(Key.Enter));
        CommandBindings.Add(new CommandBinding(confirmCommand, OKButton_Click));

        var cancelCommand = new RoutedCommand();
        cancelCommand.InputGestures.Add(new KeyGesture(Key.Escape));
        CommandBindings.Add(new CommandBinding(cancelCommand, CancelDialog));
    }

    public string ResponseText
    {
        get => ResponseTextBox.Text;
        set => ResponseTextBox.Text = value;
    }

    private void CancelDialog(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResponseTextBox.Text)) return;
        DialogResult = true;
    }
}
