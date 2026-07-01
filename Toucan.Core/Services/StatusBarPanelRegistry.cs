using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Registry managing all status bar panels. Panels self-register here.
/// The ViewModel observes the Panels collection to render them dynamically.
/// </summary>
public partial class StatusBarPanelRegistry : ObservableObject
{
    private static StatusBarPanelRegistry? _instance;
    public static StatusBarPanelRegistry Instance => _instance ??= new StatusBarPanelRegistry();

    /// <summary>All registered panels, sorted by alignment then order.</summary>
    public ObservableCollection<IStatusBarPanel> Panels { get; } = [];

    /// <summary>Left-aligned panels (filtered view for binding).</summary>
    public ObservableCollection<IStatusBarPanel> LeftPanels { get; } = [];

    /// <summary>Center-aligned panels (filtered view for binding).</summary>
    public ObservableCollection<IStatusBarPanel> CenterPanels { get; } = [];

    /// <summary>Right-aligned panels (filtered view for binding).</summary>
    public ObservableCollection<IStatusBarPanel> RightPanels { get; } = [];

    private StatusBarPanelRegistry() { }

    /// <summary>Register a panel. Inserts in order within its alignment group.</summary>
    public void Register(IStatusBarPanel panel)
    {
        if (Panels.Any(p => p.Id == panel.Id)) return; // no duplicates

        Panels.Add(panel);
        GetCollection(panel.Alignment).Add(panel);
        SortCollection(panel.Alignment);
    }

    /// <summary>Unregister a panel by ID.</summary>
    public void Unregister(string panelId)
    {
        var panel = Panels.FirstOrDefault(p => p.Id == panelId);
        if (panel == null) return;

        Panels.Remove(panel);
        GetCollection(panel.Alignment).Remove(panel);
    }

    /// <summary>Get a panel by ID.</summary>
    public IStatusBarPanel? Get(string panelId) => Panels.FirstOrDefault(p => p.Id == panelId);

    /// <summary>Get a typed panel by ID.</summary>
    public T? Get<T>(string panelId) where T : class, IStatusBarPanel => Get(panelId) as T;

    /// <summary>Show or hide a panel.</summary>
    public void SetVisibility(string panelId, bool visible)
    {
        var panel = Get(panelId);
        if (panel != null) panel.IsVisible = visible;
    }

    /// <summary>Move a panel to a new order position within its alignment group.</summary>
    public void Reorder(string panelId, int newOrder)
    {
        var panel = Get(panelId);
        if (panel == null) return;
        panel.Order = newOrder;
        SortCollection(panel.Alignment);
    }

    private ObservableCollection<IStatusBarPanel> GetCollection(StatusBarAlignment alignment) => alignment switch
    {
        StatusBarAlignment.Left => LeftPanels,
        StatusBarAlignment.Center => CenterPanels,
        StatusBarAlignment.Right => RightPanels,
        _ => LeftPanels
    };

    private void SortCollection(StatusBarAlignment alignment)
    {
        var col = GetCollection(alignment);
        var sorted = col.OrderBy(p => p.Order).ToList();
        col.Clear();
        foreach (var p in sorted) col.Add(p);
    }
}
