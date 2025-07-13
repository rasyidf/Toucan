using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
