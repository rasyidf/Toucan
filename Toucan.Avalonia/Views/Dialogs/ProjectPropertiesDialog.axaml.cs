using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ProjectPropertiesDialog : DialogWindow
{
    public ProjectPropertiesDialog() => InitializeComponent();

    public ProjectPropertiesDialog(ProjectPropertiesViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = ok => Close(ok);
    }
}
