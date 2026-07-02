using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toucan.ViewModels;

namespace Toucan.Views;

public partial class StartScreen : UserControl
{
    public StartScreen()
    {
        InitializeComponent();
    }

    private void RecentProject_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && DataContext is MainWindowViewModel vm)
        {
            if (vm.OpenRecentProjectCommand.CanExecute(path))
            {
                vm.OpenRecentProjectCommand.Execute(path);
            }
        }
    }

    private void RemoveRecent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && DataContext is MainWindowViewModel vm)
        {
            // Remove from recent list and refresh
            vm.RemoveRecentProjectCommand?.Execute(path);
        }
    }
}
