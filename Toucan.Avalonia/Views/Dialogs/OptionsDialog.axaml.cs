using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class OptionsDialog : Window
{
    public OptionsDialog() => InitializeComponent();

    public OptionsDialog(OptionsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = result => Close(result);
    }
}
