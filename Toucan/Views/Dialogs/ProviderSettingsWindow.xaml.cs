using System.Windows;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs
{
    public partial class ProviderSettingsWindow : FluentWindow
    {
        public ProviderSettingsWindow()
        {
            InitializeComponent();

            // resolve dependencies and create the VM
            var svc = Toucan.App.Services.GetService(typeof(Toucan.Services.IProviderSettingsService)) as Toucan.Services.IProviderSettingsService;
            var secure = Toucan.App.Services.GetService(typeof(Toucan.Services.ISecureStorageService)) as Toucan.Services.ISecureStorageService;
            var dialogs = Toucan.App.Services.GetService(typeof(Toucan.Services.IDialogService)) as Toucan.Services.IDialogService;

            if (svc != null && secure != null && dialogs != null)
                DataContext = new ProviderSettingsViewModel(svc, secure, dialogs);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProviderSettingsViewModel vm)
            {
                vm.SaveCommand?.Execute(null);
            }

            this.DialogResult = true;
            Close();
        }
    }
}
