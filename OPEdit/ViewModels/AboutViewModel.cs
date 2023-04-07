using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Windows;

namespace OPEdit.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly Window Window;
    public AboutViewModel(Window window) {
        this.Window = window;
    }

    public string AppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    [RelayCommand]
    public void Close()
    {
        Window.Close();
    }
}