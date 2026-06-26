using Avalonia.Controls;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

public partial class PreTranslateDialog : Window
{
    public PreTranslateDialog() => InitializeComponent();

    public PreTranslateDialog(PreTranslateViewModel vm) : this()
    {
        DataContext = vm;
        CancelBtn.Click += (_, _) => Close(false);
        CommitBtn.Click += (_, _) => Close(true);
    }
}
