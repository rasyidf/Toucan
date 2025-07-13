using MahApps.Metro.Controls;
using Toucan.Contracts.Views;
using Toucan.ViewModels; 
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
namespace Toucan.Views;

public partial class ShellDialogWindow : FluentWindow, IShellDialogWindow
{
    public ShellDialogWindow(ShellDialogViewModel viewModel)
    {
        InitializeComponent();
        viewModel.SetResult = OnSetResult;
        DataContext = viewModel;
    }

    public Frame GetDialogFrame()
        => dialogFrame;

    private void OnSetResult(bool? result)
    {
        DialogResult = result;
        Close();
    }
}
