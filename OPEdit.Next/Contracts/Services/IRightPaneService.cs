using System.Windows.Controls;

using MahApps.Metro.Controls;
using Wpf.Ui.Controls;

namespace OPEdit.Contracts.Services;

public interface IRightPaneService
{
    event EventHandler PaneOpened;

    event EventHandler PaneClosed;

    void OpenDialog(string pageKey, object parameter = null);

    void Initialize(Frame rightPaneFrame, Dialog splitView);

    void CleanUp();
}
