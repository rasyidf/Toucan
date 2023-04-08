using System.Windows.Input;
using Wpf.Ui.Controls.Window;

namespace OPEdit;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
partial class NewProjectPrompt : FluentWindow
{



    public NewProjectPrompt(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
         
        ResponseTextBox.Text = defaultValue;
        ResponseTextBox.Focus();
        ResponseTextBox.SelectAll();

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));



    }

    public string ResponseText
    {
        get { return ResponseTextBox.Text; }
        set { ResponseTextBox.Text = value; }
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
