using System.Windows;
using System.Windows.Controls;
using Toucan.ViewModels;

namespace Toucan.Views.NewProject;

public partial class FrameworkStep : UserControl
{
    public FrameworkStep()
    {
        InitializeComponent();
    }

    private void FrameworkTile_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is FrameworkTile tile)
        {
            if (DataContext is NewProjectViewModel vm)
            {
                vm.SelectedFramework = tile;
            }
        }
    }
}
