using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class PromptDialog : Window
{
    public string ResponseText => ResponseTextBox.Text ?? string.Empty;

    public PromptDialog() => InitializeComponent();

    public PromptDialog(string title, string message, string defaultValue = "") : this()
    {
        Title = title;
        MessageLabel.Text = message;
        ResponseTextBox.Text = defaultValue;

        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Close(true);
            if (e.Key == Key.Escape) Close(false);
        };
    }
}
