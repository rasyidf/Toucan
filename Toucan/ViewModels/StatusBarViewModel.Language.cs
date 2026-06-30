using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>Default language panel: shows the active project/app language.</summary>
internal partial class StatusBarViewModel
{
    [ObservableProperty]
    private string defaultLanguage = "en-US";

    [ObservableProperty]
    private string cursorPosition = "Ln 0, Col 0";

    [ObservableProperty]
    private string modeText = "Development *";
}
