using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Views.Components;

public partial class ResourcesView : UserControl
{
    public ResourcesView() => InitializeComponent();

    private void TreeNamespace_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tv && tv.SelectedItem is NsTreeItem item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectedNode = item;
                vm.Search(item.Namespace, false);
            }
        }
    }

    private void ListNamespace_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is NsTreeItem item)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SelectedNode = item;
                vm.Search(item.Namespace, false);
            }
        }
    }
}
