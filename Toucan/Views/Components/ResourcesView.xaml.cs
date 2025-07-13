using System.Windows;
using System.Windows.Controls;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for ResourcesView.xaml
/// </summary>
public partial class ResourcesView : UserControl
{ 
    public event RoutedPropertyChangedEventHandler<object> SelectionChanged;
    public ResourcesView()
    {
        InitializeComponent();
    }
     

    private void TreeNamespace_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        SelectionChanged?.Invoke(sender, e);
    }
}
