using Avalonia.Controls;
using Avalonia.Interactivity;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class ProjectSettingsDialog : Window
{
    public ProjectSettingsDialog() => InitializeComponent();

    public ProjectSettingsDialog(ProjectSettingsDialogViewModel vm) : this()
    {
        DataContext = vm;
        SaveBtn.Click += (_, _) => { vm.Apply(); Close(true); };
        CancelBtn.Click += (_, _) => Close(false);
        AddLangBtn.Click += (_, _) =>
        {
            var lang = NewLangBox.Text?.Trim();
            if (!string.IsNullOrEmpty(lang)) { vm.AddLanguage(lang); NewLangBox.Text = ""; }
        };
    }
}
