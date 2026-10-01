using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class PreTranslateDialog : DialogWindow
{
    public PreTranslateDialog() => InitializeComponent();

    public PreTranslateDialog(PreTranslateViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = ok => Close(ok);
    }
}
