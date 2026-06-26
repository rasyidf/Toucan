using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class LanguageManagerDialog : Window
{
    public LanguageManagerDialog() => InitializeComponent();

    public LanguageManagerDialog(LanguageManagerViewModel vm) : this()
    {
        DataContext = vm;
        AddBtn.Click += (_, _) =>
        {
            var lang = NewLangInput.Text?.Trim();
            if (!string.IsNullOrEmpty(lang)) { vm.AddLanguage(lang); NewLangInput.Text = ""; }
        };
        MoveUpBtn.Click += (_, _) => vm.MoveUp();
        MoveDownBtn.Click += (_, _) => vm.MoveDown();
        SetPrimaryBtn.Click += (_, _) => vm.SetPrimary();
        SaveBtn.Click += (_, _) => { vm.Save(); Close(true); };
        CloseBtn.Click += (_, _) => Close(false);
    }
}
