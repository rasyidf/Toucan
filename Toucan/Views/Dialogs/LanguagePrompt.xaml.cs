using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Toucan.Core;
using Toucan.Core.Models;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
public partial class LanguagePrompt : FluentWindow
{
    public List<TranslationItem>? LanguageList { get; set; }

    public LanguagePromptViewModel ViewModel { get; }

    public LanguagePrompt(string title, string message, List<TranslationItem>? languageList, Func<IEnumerable<TranslationItem>, LanguagePromptViewModel> viewModelFactory)
    {
        InitializeComponent();
        titleBarPrompt.Title = title;
        messageLabel.Text = message;
        _ = ResponseLanguage.Focus();

        RoutedCommand saveCommand = new();
        _ = saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        _ = refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));

        ViewModel = viewModelFactory(languageList ?? []);
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

    public string? ResponseText
    {
        get
        {
            // Prefer to return the culture code (e.g., en-US) when a suggestion was chosen.
            if (ViewModel?.SelectedLanguage?.Culture != null)
            {
                return ViewModel.SelectedLanguage.Culture.Name;
            }

            // If user typed a display/culture name, try to resolve it to a culture code
            string? typed = ResponseLanguage?.Text;
            if (!string.IsNullOrWhiteSpace(typed) && ViewModel?.CultureList != null)
            {
                var match = ViewModel.CultureList.FirstOrDefault(l => string.Equals(l.Language, typed, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Culture?.NativeName ?? string.Empty, typed, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Culture?.Name ?? string.Empty, typed, System.StringComparison.OrdinalIgnoreCase));
                if (match?.Culture != null)
                {
                    return match.Culture.Name;
                }
            }

            // Fallback to typed text
            return typed;
        }

        set => ResponseLanguage.Text = value ?? string.Empty;
    }

    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
    private async void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        string? response = ResponseText;
        if (string.IsNullOrWhiteSpace(response))
        {
            var msgBox = new Wpf.Ui.Controls.MessageBox { Title = "Validation", Content = "Please select or enter a language.", PrimaryButtonText = "OK", CloseButtonText = string.Empty };
            await msgBox.ShowDialogAsync();
            return;
        }
        // If existing language passed and the new language already exists — warn and don't close.
        if (LanguageList != null && LanguageList.Any(l => string.Equals(l.Language, response, System.StringComparison.InvariantCultureIgnoreCase)))
        {
            var msgBox = new Wpf.Ui.Controls.MessageBox { Title = "Validation", Content = "Language already exists.", PrimaryButtonText = "OK", CloseButtonText = string.Empty };
            await msgBox.ShowDialogAsync();
            return;
        }
        DialogResult = true;
    }
}
