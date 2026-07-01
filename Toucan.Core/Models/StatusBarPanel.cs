using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Core.Models;

/// <summary>
/// Alignment position for a status bar panel.
/// </summary>
public enum StatusBarAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Contract for a modular status bar panel.
/// Each panel is an independent unit that can be shown/hidden, reordered, and clicked.
/// </summary>
public interface IStatusBarPanel
{
    /// <summary>Unique identifier for this panel (used for persistence and lookup).</summary>
    string Id { get; }

    /// <summary>Display priority (lower = further left within its alignment group).</summary>
    int Order { get; set; }

    /// <summary>Which side of the status bar this panel belongs to.</summary>
    StatusBarAlignment Alignment { get; }

    /// <summary>Whether the panel is currently visible.</summary>
    bool IsVisible { get; set; }

    /// <summary>Primary display text shown in the status bar.</summary>
    string Content { get; }

    /// <summary>Secondary icon name (Wpf.Ui SymbolRegular name).</summary>
    string? Icon { get; }

    /// <summary>Tooltip text or rich content description.</summary>
    string? ToolTip { get; }

    /// <summary>Optional badge text (e.g., error count, dirty count).</summary>
    string? Badge { get; }

    /// <summary>Badge severity for styling (null = accent, "error" = red, "warning" = yellow, "success" = green).</summary>
    string? BadgeSeverity { get; }

    /// <summary>Command executed when the panel is clicked.</summary>
    ICommand? ClickCommand { get; }

    /// <summary>Command parameter for the click command.</summary>
    object? ClickCommandParameter { get; }
}

/// <summary>
/// Base class for status bar panels with built-in INPC support.
/// </summary>
public abstract partial class StatusBarPanelBase : ObservableObject, IStatusBarPanel
{
    public abstract string Id { get; }

    [ObservableProperty]
    private int order;

    public abstract StatusBarAlignment Alignment { get; }

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private string content = "";

    [ObservableProperty]
    private string? icon;

    [ObservableProperty]
    private string? toolTip;

    [ObservableProperty]
    private string? badge;

    [ObservableProperty]
    private string? badgeSeverity;

    public virtual ICommand? ClickCommand => null;
    public virtual object? ClickCommandParameter => null;
}
