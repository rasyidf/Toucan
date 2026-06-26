using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Avalonia.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty] private string modeText = "Development *";
    [ObservableProperty] private string projectName = "Toucan Project";
    [ObservableProperty] private string cursorPosition = "Ln 0, Col 0";
    [ObservableProperty] private string defaultLanguage = "en-US";
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int notificationCount;
}
