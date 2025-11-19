using System.Windows;
using System.Windows.Controls;

namespace Toucan.Views
{
    /// <summary>
    /// Interaction logic for ToolBarView.xaml
    /// </summary>
    public partial class ToolBarView : UserControl
    {
        public ToolBarView()
        {
            InitializeComponent();
        }

        private void OpenDropDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.IsOpen = true;
            }
        }
    }
}
