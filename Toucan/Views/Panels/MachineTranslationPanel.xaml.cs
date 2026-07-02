using System.Windows;
using System.Windows.Controls;
using Toucan.Core.Options;
using Toucan.ViewModels;

namespace Toucan.Views.Panels;

public partial class MachineTranslationPanel : UserControl
{
    public MachineTranslationPanel() => InitializeComponent();

    private void SwitchProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string provider })
            return;

        if (DataContext is MainWindowViewModel vm && vm.AppOptions != null)
        {
            vm.AppOptions.LastProvider = provider;
            vm.AppOptions.ToDisk();
            // Reload to get a fresh instance — triggers [ObservableProperty] change notification
            vm.AppOptions = AppOptions.LoadFromDisk();
        }
    }
}
