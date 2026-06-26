using Avalonia.Controls;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel vm, StatusBarViewModel statusVm)
    {
        InitializeComponent();
        DataContext = vm;
        RootStatusBar.DataContext = statusVm;
        StatusBarService.Instance.Register(statusVm);
    }
}
