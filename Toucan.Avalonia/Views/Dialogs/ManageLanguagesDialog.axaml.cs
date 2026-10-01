using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ManageLanguagesDialog : DialogWindow
{
    public ManageLanguagesDialog() => InitializeComponent();

    public ManageLanguagesDialog(LanguageManagerViewModel vm) : this()
    {
        DataContext = vm;
        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        CultureList.DoubleTapped += (_, _) => vm.AddLanguageCommand.Execute(vm.SelectedCulture);
    }
}
