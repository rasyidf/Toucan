using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
public partial class PromptDialog : FluentWindow
{



    public PromptDialog(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        titleBarPrompt.Title = title;
        messageLabel.Text = message;
        ResponseTextBox.Text = defaultValue;
        _ = ResponseTextBox.Focus();
        ResponseTextBox.SelectAll();

        RoutedCommand saveCommand = new();
        _ = saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        _ = refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));



    }

    public string ResponseText
    { get => ResponseTextBox.Text; set => ResponseTextBox.Text = value;
    }


    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
    private void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
