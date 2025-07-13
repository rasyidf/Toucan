using System.Windows.Controls;

using MahApps.Metro.Controls;
using Wpf.Ui.Controls;

namespace Toucan.Contracts.Views;

public interface IShellWindow
{
    Frame GetNavigationFrame();

    void ShowWindow();

    void CloseWindow();
     
}
