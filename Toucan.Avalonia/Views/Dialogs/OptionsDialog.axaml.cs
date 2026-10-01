using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class OptionsDialog : DialogWindow
{
    public OptionsDialog() => InitializeComponent();

    public OptionsDialog(OptionsViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = ok => Close(ok);
    }
}
