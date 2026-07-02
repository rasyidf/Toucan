using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Core.Models;

public enum SidePanelSlot { Left, Right }

public interface ISidePanel
{
    string Id { get; }
    string Title { get; }
    string Icon { get; }
    SidePanelSlot DefaultSlot { get; }
    int Order { get; set; }
    bool IsActive { get; set; }
    bool IsVisible { get; set; }
}

public abstract partial class SidePanelBase : ObservableObject, ISidePanel
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Icon { get; }
    public abstract SidePanelSlot DefaultSlot { get; }

    [ObservableProperty] private int order;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private bool isVisible = true;
}
