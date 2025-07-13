using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.ViewModels;

public partial class ShellDialogViewModel : ObservableObject
{

    [ObservableProperty]
    private string title;

    public Action<bool?> SetResult { get; set; }

    public ShellDialogViewModel()
    {
    }

    [RelayCommand]
    private void Close()
    {
        bool result = true;
        SetResult(result);
    }
}
