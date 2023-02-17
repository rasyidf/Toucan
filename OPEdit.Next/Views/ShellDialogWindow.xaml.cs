using System.Windows.Controls;

using MahApps.Metro.Controls;

using OPEdit.Contracts.Views;
using OPEdit.ViewModels;
using Wpf.Ui.Controls.Window;

namespace OPEdit.Views;

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
