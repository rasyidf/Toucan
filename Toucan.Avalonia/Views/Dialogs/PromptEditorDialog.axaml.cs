using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class PromptEditorDialog : DialogWindow
{
    public PromptEditorDialog() => InitializeComponent();

    public PromptEditorDialog(PromptEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseAction = saved => Close(saved);
    }
}
