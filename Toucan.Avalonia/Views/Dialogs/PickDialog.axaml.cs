using Avalonia.Input;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Pick one item from a list. Closes with the selected string, or null.</summary>
public partial class PickDialog : DialogWindow
{
    public PickDialog() : this(string.Empty, string.Empty, [], null) { }

    public PickDialog(string title, string message, IReadOnlyList<string> options, string? selected)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        Options.ItemsSource = options;
        Options.SelectedItem = selected ?? options.FirstOrDefault();
        Options.DoubleTapped += (_, _) => Accept();
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => Options.Focus(NavigationMethod.Tab);
    }

    private void Accept()
    {
        if (Options.SelectedItem is string s) Close(s);
    }
}
