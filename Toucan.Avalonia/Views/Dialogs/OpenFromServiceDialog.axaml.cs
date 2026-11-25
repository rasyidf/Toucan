using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Pick a service project and a folder; Toucan creates the local project and closes the dialog.</summary>
public partial class OpenFromServiceDialog : DialogWindow
{
    public OpenFromServiceDialog() => InitializeComponent();

    public OpenFromServiceDialog(OpenFromServiceViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
        // Typing in the box is a decision about the folder; the default stops following the project name.
        FolderBox.LostFocus += (_, _) =>
        {
            if (FolderBox.Text is { } typed && typed != vm.Folder) vm.FolderChangedByUser(typed);
        };
    }
}
