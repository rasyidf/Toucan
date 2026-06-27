using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Windows;

namespace Toucan.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly Window Window;
    public AboutViewModel(Window window)
    {
        Window = window;
    }

    // Null-forgiving: GetExecutingAssembly().GetName().Version is non-null for a built WPF app with AssemblyVersion set
    public string AppVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

    [RelayCommand]
    public void Close()
    {
        Window.Close();
    }
}