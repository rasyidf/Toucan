using System.Linq;
using System.Windows;
using Toucan.Core;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

/// <summary>
/// Manage Languages dialog — add, remove, reorder, and set primary language.
/// </summary>
public partial class ManageLanguagesDialog : FluentWindow
{
    public LanguageManagerViewModel ViewModel { get; }

    public ManageLanguagesDialog(LanguageManagerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void AddLanguageBox_SuggestionChosen(object sender, RoutedEventArgs e)
    {
        if (e is AutoSuggestBoxSuggestionChosenEventArgs args && args.SelectedItem is LanguageModel model)
        {
            ViewModel.AddLanguageCommand.Execute(model);
            AddLanguageBox.Text = string.Empty;
            ViewModel.FilterText = string.Empty;
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // Try to add by typed text if no suggestion was chosen
        var text = AddLanguageBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            // Find matching culture from filtered list
            var match = ViewModel.FilteredCultures.FirstOrDefault(c =>
                string.Equals(c.Culture?.Name, text, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Language, text, System.StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                ViewModel.AddLanguageCommand.Execute(match);
            }
            else
            {
                // Allow adding custom language codes
                ViewModel.AddLanguageByCode(text);
            }

            AddLanguageBox.Text = string.Empty;
            ViewModel.FilterText = string.Empty;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
