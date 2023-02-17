using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OPEdit.Views
{
    /// <summary>
    /// Interaction logic for ResourcesView.xaml
    /// </summary>
    public partial class ResourcesView : UserControl
    {
        public event RoutedEventHandler NewItem;
        public event RoutedPropertyChangedEventHandler<object> SelectionChanged;
        public ResourcesView()
        {
            InitializeComponent();
        }

        private void HandleNewItem(object sender, RoutedEventArgs e)
        {
            NewItem?.Invoke(sender, e);
        }

        private void TreeNamespace_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectionChanged?.Invoke(sender, e);
        }
    }
}
