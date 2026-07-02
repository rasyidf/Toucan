using System.Windows;
using System.Windows.Controls;
using Toucan.ViewModels;

namespace Toucan.Views.Settings;

public partial class LanguagesSettingsPage : UserControl
{
    public LanguagesSettingsPage()
    {
        InitializeComponent();
    }

    private void AddSuggestedLanguage_Click(object sender, RoutedEventArgs e)
    {
        string? lang = NewSuggestedLangBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(lang))
        {
            return;
        }

        if (DataContext is OptionsViewModel vm)
        {
            if (vm.SuggestedLanguages.Contains(lang))
            {
                return;
            }

            vm.SuggestedLanguages.Add(lang);
        }

        NewSuggestedLangBox.Text = string.Empty;
    }
}
