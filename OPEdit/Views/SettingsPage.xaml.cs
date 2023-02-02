using System.Windows.Controls;

using OPEdit.ViewModels;

namespace OPEdit.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
