using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public partial class SidePanelRegistry : ObservableObject
{
    private static SidePanelRegistry? _instance;
    public static SidePanelRegistry Instance => _instance ??= new SidePanelRegistry();

    public ObservableCollection<ISidePanel> LeftSlotPanels { get; } = [];
    public ObservableCollection<ISidePanel> RightSlotPanels { get; } = [];

    [ObservableProperty] private ISidePanel? activeLeftPanel;
    [ObservableProperty] private ISidePanel? activeRightPanel;
    [ObservableProperty] private bool leftSlotVisible = true;
    [ObservableProperty] private bool rightSlotVisible = true;

    public void Register(ISidePanel panel)
    {
        var col = panel.DefaultSlot == SidePanelSlot.Left ? LeftSlotPanels : RightSlotPanels;
        if (col.Any(p => p.Id == panel.Id)) return;
        col.Add(panel);
        SortCollection(col);

        // Auto-activate first panel in slot
        if (panel.DefaultSlot == SidePanelSlot.Left && ActiveLeftPanel == null)
            Activate(panel.Id);
        else if (panel.DefaultSlot == SidePanelSlot.Right && ActiveRightPanel == null)
            Activate(panel.Id);
    }

    public void Unregister(string panelId)
    {
        var panel = Find(panelId);
        if (panel == null) return;
        var col = panel.DefaultSlot == SidePanelSlot.Left ? LeftSlotPanels : RightSlotPanels;
        col.Remove(panel);
        if (ActiveLeftPanel?.Id == panelId) ActiveLeftPanel = LeftSlotPanels.FirstOrDefault();
        if (ActiveRightPanel?.Id == panelId) ActiveRightPanel = RightSlotPanels.FirstOrDefault();
    }

    public void Activate(string panelId)
    {
        var panel = Find(panelId);
        if (panel == null) return;

        if (panel.DefaultSlot == SidePanelSlot.Left)
        {
            if (ActiveLeftPanel != null) ActiveLeftPanel.IsActive = false;
            panel.IsActive = true;
            ActiveLeftPanel = panel;
            LeftSlotVisible = true;
        }
        else
        {
            if (ActiveRightPanel != null) ActiveRightPanel.IsActive = false;
            panel.IsActive = true;
            ActiveRightPanel = panel;
            RightSlotVisible = true;
        }
    }

    public void Toggle(string panelId)
    {
        var panel = Find(panelId);
        if (panel == null) return;

        if (panel.IsActive)
        {
            // Hide the slot
            if (panel.DefaultSlot == SidePanelSlot.Left) LeftSlotVisible = !LeftSlotVisible;
            else RightSlotVisible = !RightSlotVisible;
        }
        else
        {
            Activate(panelId);
        }
    }

    public void ToggleLeftSlot() => LeftSlotVisible = !LeftSlotVisible;
    public void ToggleRightSlot() => RightSlotVisible = !RightSlotVisible;

    private ISidePanel? Find(string id) =>
        LeftSlotPanels.FirstOrDefault(p => p.Id == id) ?? RightSlotPanels.FirstOrDefault(p => p.Id == id);

    private static void SortCollection(ObservableCollection<ISidePanel> col)
    {
        var sorted = col.OrderBy(p => p.Order).ToList();
        col.Clear();
        foreach (var p in sorted) col.Add(p);
    }
}
