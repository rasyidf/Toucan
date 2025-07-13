using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class HomePage : Page
{
    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
