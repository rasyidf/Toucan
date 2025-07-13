using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class SearchPage : Page
{
    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
