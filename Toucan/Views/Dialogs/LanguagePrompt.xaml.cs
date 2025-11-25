using System.Collections.Generic;
using System.Windows.Input;
using Wpf.Ui.Controls;
using Toucan.ViewModels;
using Toucan.Core.Models;
using Toucan.Core;
using System.Linq;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
partial class LanguagePrompt : FluentWindow
{
    public List<TranslationItem> LanguageList { get; set; }

    public LanguagePromptViewModel ViewModel { get; }

    public LanguagePrompt(string title, string message, List<TranslationItem> languageList)
    {
        InitializeComponent();
        titleBarPrompt.Title = title;
        messageLabel.Text = message;
        ResponseLanguage.Focus();

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));

        ViewModel = new LanguagePromptViewModel(languageList);
        LanguageList = languageList;
        DataContext = ViewModel;

        // Wire up suggestions selection to map to selected language in the view model
        ResponseLanguage.SuggestionChosen += (s, args) =>
        {
            if (args?.SelectedItem is LanguageModel model)
            {
                ViewModel.SelectedLanguage = model;
                // Set the Text to the display language so the user sees a friendly name
                ResponseLanguage.Text = model.Language;
            }
        };
    }

    public string ResponseText
    {
        get
        {
            // Prefer to return the culture code (e.g., en-US) when a suggestion was chosen.
            if (ViewModel?.SelectedLanguage?.Culture != null)
                return ViewModel.SelectedLanguage.Culture.Name;

            // If user typed a display/culture name, try to resolve it to a culture code
            string typed = ResponseLanguage?.Text;
            if (!string.IsNullOrWhiteSpace(typed) && ViewModel?.CultureList != null)
            {
                var match = ViewModel.CultureList.FirstOrDefault(l => string.Equals(l.Language, typed, System.StringComparison.InvariantCultureIgnoreCase) ||
                    string.Equals(l.Culture?.NativeName ?? string.Empty, typed, System.StringComparison.InvariantCultureIgnoreCase) ||
                    string.Equals(l.Culture?.Name ?? string.Empty, typed, System.StringComparison.InvariantCultureIgnoreCase));
                if (match?.Culture != null)
                    return match.Culture.Name;
            }

            // Fallback to typed text
            return typed;
        }
        set { ResponseLanguage.Text = value; }
    }

    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
    private void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        string response = ResponseText;
        if (string.IsNullOrWhiteSpace(response))
        {
            System.Windows.MessageBox.Show("Please select or enter a language.");
            return;
        }
        // If existing language passed and the new language already exists — warn and don't close.
        if (LanguageList != null && LanguageList.Any(l => string.Equals(l.Language, response, System.StringComparison.InvariantCultureIgnoreCase)))
        {
            System.Windows.MessageBox.Show("Language already exists.");
            return;
        }
        DialogResult = true;
    }
}
