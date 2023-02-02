using System.Windows.Controls;

using MahApps.Metro.Controls;
using Wpf.Ui.Controls;

namespace OPEdit.Contracts.Views;

public interface IShellWindow
{
    Frame GetNavigationFrame();

    void ShowWindow();

    void CloseWindow();

    Frame GetRightPaneFrame();

    Dialog GetSplitView();
}
