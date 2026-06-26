using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ProviderSettingsDialog : Window
{
    public ProviderSettingsDialog() => InitializeComponent();

    public ProviderSettingsDialog(ProviderSettingsViewModel vm) : this()
    {
        DataContext = vm;
        CloseBtn.Click += (_, _) => Close();
    }
}
