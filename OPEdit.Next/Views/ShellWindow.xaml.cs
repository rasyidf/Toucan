using System.Windows.Controls;

using MahApps.Metro.Controls;

using OPEdit.Contracts.Views;
using OPEdit.ViewModels;
using Wpf.Ui.Controls;

namespace OPEdit.Views;

public partial class ShellWindow : UiWindow, IShellWindow
{
    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public Frame GetNavigationFrame()
        => shellFrame;

    public Frame GetRightPaneFrame()
        => rightPaneFrame;

    public void ShowWindow()
        => Show();

    public void CloseWindow()
        => Close();

    public Dialog GetSplitView()
        => splitView;
}
