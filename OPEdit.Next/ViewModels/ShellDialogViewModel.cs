using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OPEdit.ViewModels;

public class ShellDialogViewModel : ObservableObject
{
    private ICommand _closeCommand;

    public ICommand CloseCommand => _closeCommand ?? (_closeCommand = new RelayCommand(OnClose));

    public Action<bool?> SetResult { get; set; }

    public ShellDialogViewModel()
    {
    }

    private void OnClose()
    {
        bool result = true;
        SetResult(result);
    }
}
