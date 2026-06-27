using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Toucan.Services;

/// <summary>
/// Manages visibility of all editor panels (suggestions, focused editor, zen mode).
/// ponytail: singleton observable — panels bind directly to these properties.
/// </summary>
internal partial class PanelService : ObservableObject
{
    private static readonly Lazy<PanelService> s_instance = new(() => new PanelService());
    public static PanelService Instance => s_instance.Value;

    [ObservableProperty] private bool suggestionsVisible;
    [ObservableProperty] private bool focusedEditorVisible;
    [ObservableProperty] private bool zenMode;
    [ObservableProperty] private bool toolbarVisible = true;
    [ObservableProperty] private bool statusBarVisible = true;
    [ObservableProperty] private bool sidePanelVisible = true;

    public void ToggleSuggestions()
    {
        SuggestionsVisible = !SuggestionsVisible;
    }

    public void ToggleFocusedEditor()
    {
        FocusedEditorVisible = !FocusedEditorVisible;
    }

    public void EnterZenMode()
    {
        ZenMode = true;
        ToolbarVisible = false;
        StatusBarVisible = false;
        SidePanelVisible = false;
        SuggestionsVisible = false;
    }

    public void ExitZenMode()
    {
        ZenMode = false;
        ToolbarVisible = true;
        StatusBarVisible = true;
        SidePanelVisible = true;
    }

    public void ToggleZenMode()
    {
        if (ZenMode)
        {
            ExitZenMode();
        }
        else
        {
            EnterZenMode();
        }
    }
}
