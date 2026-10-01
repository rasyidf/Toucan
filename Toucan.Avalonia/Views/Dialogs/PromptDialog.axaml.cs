namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Single-line text prompt. Closes with the entered text, or null when cancelled.</summary>
public partial class PromptDialog : DialogWindow
{
    public PromptDialog() : this(string.Empty, string.Empty) { }

    public PromptDialog(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.IsVisible = !string.IsNullOrEmpty(message);
        Input.Text = defaultValue;
        OkButton.Click += (_, _) => Close(Input.Text ?? string.Empty);
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.CaretIndex = Input.Text?.Length ?? 0;
        };
    }
}
