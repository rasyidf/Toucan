using System.Windows;
using System.Windows.Controls;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs
{
    public partial class ProviderSettingsWindow : FluentWindow
    {
        public ProviderSettingsWindow(ProviderSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProviderSettingsViewModel vm)
            {
                vm.SaveCommand?.Execute(null);
            }

            DialogResult = true;
            Close();
        }

        private void AddProvider_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }
    }
}
