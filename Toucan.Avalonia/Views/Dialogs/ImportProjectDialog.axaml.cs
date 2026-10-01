using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ImportProjectDialog : DialogWindow
{
    public ImportProjectDialog() => InitializeComponent();

    public ImportProjectDialog(ImportProjectViewModel vm) : this()
    {
        DataContext = vm;
        OkButton.Click += (_, _) => { if (vm.IsValid) Close(true); };
        CancelButton.Click += (_, _) => Close(false);
    }
}
