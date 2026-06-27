using System.Windows;
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
    }
}
