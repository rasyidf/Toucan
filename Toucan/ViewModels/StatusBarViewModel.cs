using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>
/// Status bar view model split across partial classes per panel concern.
/// This file holds shared state (project name, status text, loading indicator).
/// </summary>
internal partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string projectName = "Toucan Project";

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private int notificationCount;

    [ObservableProperty]
    private int sessionDirtyCount;

    public void ShowNotification(int count) => NotificationCount = count;
}
