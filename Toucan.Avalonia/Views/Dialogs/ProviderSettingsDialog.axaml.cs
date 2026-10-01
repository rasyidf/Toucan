using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ProviderSettingsDialog : DialogWindow
{
    public ProviderSettingsDialog() => InitializeComponent();

    public ProviderSettingsDialog(ProviderSettingsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
    }
}
