using System.Windows;
using System.Windows.Controls;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for ResourcesView.xaml
/// </summary>
public partial class ResourcesView : UserControl
{ 
    public event RoutedPropertyChangedEventHandler<object> SelectionChanged;
    public event RoutedEventHandler ListSelectionChanged;

    public ResourcesView()
    {
        InitializeComponent();
    }
     

    private void TreeNamespace_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectionChanged?.Invoke(sender, e);
    }

    private void ListNamespace_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ListSelectionChanged?.Invoke(sender, e);
    }
}
