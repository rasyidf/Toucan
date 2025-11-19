using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace Toucan.ViewModels;

internal partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string modeText = "Development *";

    [ObservableProperty]
    private string projectName = "Toucan Project";

    [ObservableProperty]
    private string cursorPosition = "Ln 0, Col 0";

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool isLoading;

    // Generic counters for later: TODO add Git integration and language stats
    [ObservableProperty]
    private int errors;

    [ObservableProperty]
    private int warnings;

    [ObservableProperty]
    private int gitChanges;

    [ObservableProperty]
    private int notificationCount;

    // TODO: Consider exposing ICommand for opening Git view or navigating to warnings
    public void ShowNotification(int count)
    {
        NotificationCount = count;
    }

    // TODO: wire up to the real Git service. This should update GitChanges, errors and warnings.
    public void UpdateGitStats(int changes, int errorCount, int warningCount)
    {
        GitChanges = changes;
        Errors = errorCount;
        Warnings = warningCount;
    }
}
