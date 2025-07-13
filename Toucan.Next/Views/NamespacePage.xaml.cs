using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class NamespacePage : Page
{
    public NamespacePage(NamespaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
