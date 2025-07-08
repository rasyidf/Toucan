using System.Windows.Controls;

using OPEdit.ViewModels;

namespace OPEdit.Views;

public partial class HomePage : Page
{
    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
