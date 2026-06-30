using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    /// <summary>Languages available in the current project (for inline switching).</summary>
    public ObservableCollection<string> AvailableLanguages { get; } = [];

    [RelayCommand]
    private void ChangeDefaultLanguage(string? lang)
    {
        if (!string.IsNullOrWhiteSpace(lang))
        {
            DefaultLanguage = lang;
        }
    }
}
