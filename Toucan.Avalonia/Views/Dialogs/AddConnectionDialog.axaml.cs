using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>The add-connection wizard: service, account, project, mapping, policy, test.</summary>
public partial class AddConnectionDialog : DialogWindow
{
    public AddConnectionDialog() => InitializeComponent();

    public AddConnectionDialog(AddConnectionViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
    }
}
