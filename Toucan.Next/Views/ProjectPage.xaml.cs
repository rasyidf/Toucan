using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class ProjectPage : Page
{
    public ProjectPage(ProjectViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
