using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class NewProjectDialog : DialogWindow
{
    public NewProjectDialog() : this(new NewProjectViewModel()) { }

    public NewProjectDialog(NewProjectViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseAction = ok => Close(ok);
        StepText.Text = "Step 1 of 2";
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewProjectViewModel.WizardStep)) StepText.Text = $"Step {vm.WizardStep + 1} of 2";
        };
    }
}
