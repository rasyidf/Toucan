using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Services;

/// <summary>
/// VS Code-style layout state: which side slots are visible, the status bar, zen mode, and
/// the current editor mode. Persisted to ~/Documents/Toucan/layout.json (same file as WPF).
/// </summary>
public partial class PanelService : ObservableObject
{
    private static readonly Lazy<PanelService> s_instance = new(() => new PanelService());
    public static PanelService Instance => s_instance.Value;

    private static readonly string s_layoutPath = Path.Combine(
        Toucan.Core.Services.UserDataFolder.Root, "Toucan", "layout.json");

    public SidePanelRegistry SideRegistry => SidePanelRegistry.Instance;

    [ObservableProperty] private bool statusBarVisible = true;
    [ObservableProperty] private bool zenMode;
    [ObservableProperty] private EditorMode editorMode = EditorMode.Editor;
    [ObservableProperty] private double sidebarWidth = 280;
    [ObservableProperty] private double inspectorWidth = 300;

    public bool IsEditorMode => EditorMode == EditorMode.Editor;
    public bool IsReviewMode => EditorMode == EditorMode.Review;
    public bool IsAuditMode => EditorMode == EditorMode.Audit;
    public bool IsNotAuditMode => EditorMode != EditorMode.Audit;
    public bool IsNotReviewMode => EditorMode != EditorMode.Review;
    public bool ShowsCommentButton => EditorMode != EditorMode.Editor;

    private string? _pendingLeftPanel;
    private string? _pendingRightPanel;

    private PanelService()
    {
        LoadLayout();
    }

    partial void OnEditorModeChanged(EditorMode value)
    {
        OnPropertyChanged(nameof(IsEditorMode));
        OnPropertyChanged(nameof(IsReviewMode));
        OnPropertyChanged(nameof(IsAuditMode));
        OnPropertyChanged(nameof(IsNotAuditMode));
        OnPropertyChanged(nameof(IsNotReviewMode));
        OnPropertyChanged(nameof(ShowsCommentButton));
    }

    [RelayCommand] private void ToggleSidebar() => SideRegistry.LeftSlotVisible = !SideRegistry.LeftSlotVisible;
    [RelayCommand] private void ToggleInspector() => SideRegistry.RightSlotVisible = !SideRegistry.RightSlotVisible;
    [RelayCommand] private void ToggleStatusBar() => StatusBarVisible = !StatusBarVisible;
    [RelayCommand] private void ActivatePanel(string id) => SideRegistry.Toggle(id);

    public void ToggleZenMode()
    {
        ZenMode = !ZenMode;
        StatusBarVisible = !ZenMode;
        SideRegistry.LeftSlotVisible = !ZenMode;
        SideRegistry.RightSlotVisible = !ZenMode;
    }

    /// <summary>Re-activates the panels saved in layout.json once they are registered.</summary>
    public void RestoreActivePanels()
    {
        var leftVisible = SideRegistry.LeftSlotVisible;
        var rightVisible = SideRegistry.RightSlotVisible;
        if (!string.IsNullOrEmpty(_pendingLeftPanel)) SideRegistry.Activate(_pendingLeftPanel);
        if (!string.IsNullOrEmpty(_pendingRightPanel)) SideRegistry.Activate(_pendingRightPanel);
        // Activate() forces the slot visible; keep the persisted visibility instead.
        SideRegistry.LeftSlotVisible = leftVisible;
        SideRegistry.RightSlotVisible = rightVisible;
    }

    public void SaveLayout()
    {
        try
        {
            var state = new LayoutState
            {
                StatusBarVisible = StatusBarVisible || ZenMode,
                SidePanelVisible = SideRegistry.LeftSlotVisible || ZenMode,
                InspectorVisible = SideRegistry.RightSlotVisible || ZenMode,
                SidebarWidth = SidebarWidth,
                InspectorWidth = InspectorWidth,
                EditorMode = EditorMode,
                ActiveLeftPanelId = SideRegistry.ActiveLeftPanel?.Id ?? "explorer",
                ActiveRightPanelId = SideRegistry.ActiveRightPanel?.Id ?? "languages"
            };
            Directory.CreateDirectory(Path.GetDirectoryName(s_layoutPath)!);
            File.WriteAllText(s_layoutPath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Layout is non-critical.
        }
    }

    private void LoadLayout()
    {
        try
        {
            if (!File.Exists(s_layoutPath)) return;
            var state = JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(s_layoutPath));
            if (state == null) return;

            StatusBarVisible = state.StatusBarVisible;
            SideRegistry.LeftSlotVisible = state.SidePanelVisible;
            SideRegistry.RightSlotVisible = state.InspectorVisible;
            SidebarWidth = state.SidebarWidth is > 120 and < 900 ? state.SidebarWidth : 280;
            InspectorWidth = state.InspectorWidth is > 120 and < 900 ? state.InspectorWidth : 300;
            EditorMode = state.EditorMode;
            _pendingLeftPanel = state.ActiveLeftPanelId;
            _pendingRightPanel = state.ActiveRightPanelId;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Start with defaults.
        }
    }

    private sealed class LayoutState
    {
        public bool InspectorVisible { get; set; } = true;
        public bool StatusBarVisible { get; set; } = true;
        public bool SidePanelVisible { get; set; } = true;
        public double SidebarWidth { get; set; } = 280;
        public double InspectorWidth { get; set; } = 300;
        public EditorMode EditorMode { get; set; } = EditorMode.Editor;
        public string ActiveLeftPanelId { get; set; } = "explorer";
        public string ActiveRightPanelId { get; set; } = "languages";
    }
}
