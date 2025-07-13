using System.Windows;
using System.Windows.Controls;

using MahApps.Metro.Controls;

using Toucan.Contracts.Views;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views;

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
