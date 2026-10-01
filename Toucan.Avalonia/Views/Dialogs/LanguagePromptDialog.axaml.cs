using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Searchable culture picker. Closes with a language code, or null.</summary>
public partial class LanguagePromptDialog : DialogWindow
{
    public LanguagePromptDialog() : this(new LanguagePromptViewModel()) { }

    public LanguagePromptDialog(LanguagePromptViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Title = vm.Title;
        OkButton.Click += (_, _) => Close(vm.Result);
        CancelButton.Click += (_, _) => Close(null);
        List.DoubleTapped += (_, _) => { if (vm.Result != null) Close(vm.Result); };
        Opened += (_, _) => Filter.Focus();
    }
}
