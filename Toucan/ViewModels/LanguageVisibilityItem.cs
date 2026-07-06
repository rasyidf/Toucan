using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>Checkbox item for the language visibility filter row.</summary>
public partial class LanguageVisibilityItem : ObservableObject
{
    [ObservableProperty]
    private string language = string.Empty;

    [ObservableProperty]
    private bool isVisible = true;
}
