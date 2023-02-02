using System.Windows.Controls;

using OPEdit.ViewModels;

namespace OPEdit.Views;

public partial class ProjectPage : Page
{
    public ProjectPage(ProjectViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
