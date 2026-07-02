using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.ViewModels;

namespace Toucan.Services;

/// <summary>
/// VS Code-style panel management service.
/// Manages visibility of all editor panels with layout persistence.
/// Each panel can be individually toggled. Zen mode hides all chrome.
/// Layout state is saved to disk and restored on next launch.
/// </summary>
internal partial class PanelService : ObservableObject
{
    // ponytail: backward-compat singleton until all callers are migrated to DI
    private static readonly Lazy<PanelService> s_instance = new(() => new PanelService());
    public static PanelService Instance => s_instance.Value;

    public SidePanelRegistry SideRegistry => SidePanelRegistry.Instance;

    private static readonly string s_layoutPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "layout.json");

    // --- Sidebar Panels (left) ---
    [ObservableProperty] private bool resourcesVisible = true;
    [ObservableProperty] private bool languagesVisible = true;

    // --- Main Panels (center) ---
    [ObservableProperty] private bool editorVisible = true;
    [ObservableProperty] private bool suggestionsVisible;
    [ObservableProperty] private bool focusedEditorVisible;

    // --- Right Panel (inspector) ---
    [ObservableProperty] private bool inspectorVisible = true;

    // --- Chrome ---
    [ObservableProperty] private bool toolbarVisible = true;
    [ObservableProperty] private bool statusBarVisible = true;
    [ObservableProperty] private bool sidePanelVisible = true;

    // --- Modes ---
    [ObservableProperty] private bool zenMode;

    // --- Editor Mode ---
    [ObservableProperty] private EditorMode editorMode = EditorMode.Editor;

    // --- Sidebar width (for splitter memory) ---
    [ObservableProperty] private double sidebarWidth = 200;
    [ObservableProperty] private double languagesPanelHeight = 280;

    public PanelService()
    {
        LoadLayout();
    }

    // --- Toggle Commands (bindable from menu/toolbar/keybindings) ---

    [RelayCommand] private void ToggleResources() => ResourcesVisible = !ResourcesVisible;
    [RelayCommand] private void ToggleLanguages() => LanguagesVisible = !LanguagesVisible;
    [RelayCommand] private void ToggleEditor() => EditorVisible = !EditorVisible;
    [RelayCommand] private void ToggleSuggestions() => SuggestionsVisible = !SuggestionsVisible;
    [RelayCommand] private void ToggleFocusedEditor() => FocusedEditorVisible = !FocusedEditorVisible;
    [RelayCommand] private void ToggleInspector()
    {
        InspectorVisible = !InspectorVisible;
        SideRegistry.RightSlotVisible = InspectorVisible;
    }
    [RelayCommand] private void ToggleToolbar() => ToolbarVisible = !ToolbarVisible;
    [RelayCommand] private void ToggleStatusBar() => StatusBarVisible = !StatusBarVisible;
    [RelayCommand] private void ActivateLeftPanel(string id) => SideRegistry.Toggle(id);
    [RelayCommand] private void ActivateRightPanel(string id) => SideRegistry.Toggle(id);
    [RelayCommand]
    private void ToggleSidebar()
    {
        SidePanelVisible = !SidePanelVisible;
        SideRegistry.LeftSlotVisible = SidePanelVisible;
        if (!SidePanelVisible) { ResourcesVisible = false; LanguagesVisible = false; }
        else { ResourcesVisible = true; LanguagesVisible = true; }
    }

    // --- Zen Mode ---

    [RelayCommand]
    public void ToggleZenMode()
    {
        if (ZenMode) ExitZenMode();
        else EnterZenMode();
    }

    private void EnterZenMode()
    {
        ZenMode = true;
        ToolbarVisible = false;
        StatusBarVisible = false;
        SidePanelVisible = false;
        ResourcesVisible = false;
        LanguagesVisible = false;
        SuggestionsVisible = false;
        InspectorVisible = false;
        SideRegistry.LeftSlotVisible = false;
        SideRegistry.RightSlotVisible = false;
    }

    private void ExitZenMode()
    {
        ZenMode = false;
        ToolbarVisible = true;
        StatusBarVisible = true;
        SidePanelVisible = true;
        ResourcesVisible = true;
        LanguagesVisible = true;
        InspectorVisible = true;
        SideRegistry.LeftSlotVisible = true;
        SideRegistry.RightSlotVisible = true;
    }

    // --- Layout Persistence ---

    public void SaveLayout()
    {
        try
        {
            var state = new LayoutState
            {
                ResourcesVisible = ResourcesVisible,
                LanguagesVisible = LanguagesVisible,
                EditorVisible = EditorVisible,
                SuggestionsVisible = SuggestionsVisible,
                InspectorVisible = InspectorVisible,
                ToolbarVisible = ToolbarVisible,
                StatusBarVisible = StatusBarVisible,
                SidePanelVisible = SidePanelVisible,
                SidebarWidth = SidebarWidth,
                LanguagesPanelHeight = LanguagesPanelHeight,
                EditorMode = EditorMode,
                ActiveLeftPanelId = SideRegistry.ActiveLeftPanel?.Id ?? "explorer",
                ActiveRightPanelId = SideRegistry.ActiveRightPanel?.Id ?? "inspector"
            };
            var dir = System.IO.Path.GetDirectoryName(s_layoutPath)!;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(s_layoutPath, JsonSerializer.Serialize(state));
        }
        catch { /* non-critical */ }
    }

    private void LoadLayout()
    {
        try
        {
            if (!System.IO.File.Exists(s_layoutPath)) return;
            var json = System.IO.File.ReadAllText(s_layoutPath);
            var state = JsonSerializer.Deserialize<LayoutState>(json);
            if (state == null) return;

            ResourcesVisible = state.ResourcesVisible;
            LanguagesVisible = state.LanguagesVisible;
            EditorVisible = state.EditorVisible;
            SuggestionsVisible = state.SuggestionsVisible;
            InspectorVisible = state.InspectorVisible;
            ToolbarVisible = state.ToolbarVisible;
            StatusBarVisible = state.StatusBarVisible;
            SidePanelVisible = state.SidePanelVisible;
            SidebarWidth = state.SidebarWidth > 0 ? state.SidebarWidth : 200;
            LanguagesPanelHeight = state.LanguagesPanelHeight > 0 ? state.LanguagesPanelHeight : 280;
            EditorMode = state.EditorMode;
            if (!string.IsNullOrEmpty(state.ActiveLeftPanelId))
                SideRegistry.Activate(state.ActiveLeftPanelId);
            if (!string.IsNullOrEmpty(state.ActiveRightPanelId))
                SideRegistry.Activate(state.ActiveRightPanelId);
        }
        catch { /* start with defaults */ }
    }

    private sealed class LayoutState
    {
        public bool ResourcesVisible { get; set; } = true;
        public bool LanguagesVisible { get; set; } = true;
        public bool EditorVisible { get; set; } = true;
        public bool SuggestionsVisible { get; set; }
        public bool InspectorVisible { get; set; } = true;
        public bool ToolbarVisible { get; set; } = true;
        public bool StatusBarVisible { get; set; } = true;
        public bool SidePanelVisible { get; set; } = true;
        public double SidebarWidth { get; set; } = 200;
        public double LanguagesPanelHeight { get; set; } = 280;
        public EditorMode EditorMode { get; set; } = EditorMode.Editor;
        public string ActiveLeftPanelId { get; set; } = "explorer";
        public string ActiveRightPanelId { get; set; } = "inspector";
    }
}
