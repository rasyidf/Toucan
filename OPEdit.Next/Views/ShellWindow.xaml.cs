using System.Windows.Controls;

using MahApps.Metro.Controls;

using OPEdit.Contracts.Views;
using OPEdit.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Window;

namespace OPEdit.Views;

public partial class ShellWindow : FluentWindow, IShellWindow
{
    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public Frame GetNavigationFrame()
        => shellFrame;

    public void ShowWindow()
        => Show();

    public void CloseWindow()
        => Close();
  
}
