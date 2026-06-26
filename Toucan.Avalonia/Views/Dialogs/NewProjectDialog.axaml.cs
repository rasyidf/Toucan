using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class NewProjectDialog : Window
{
    public NewProjectDialog() => InitializeComponent();

    public NewProjectDialog(NewProjectViewModel vm) : this()
    {
        DataContext = vm;
        CreateButton.Click += (_, _) =>
        {
            if (vm.IsValid) { try { vm.CreateProject(); Close(true); } catch { Close(false); } }
        };
        CancelButton.Click += (_, _) => Close(false);
    }
}
