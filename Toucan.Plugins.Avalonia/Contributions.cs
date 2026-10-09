using Avalonia.Controls;

namespace Toucan.Plugins.Desktop;

/// <summary>Which side of the window a panel lives on.</summary>
public enum PanelSlot
{
    Left,
    Right,
}

/// <summary>A button in a panel's toolbar that runs a command from the command registry.</summary>
/// <param name="CommandId">ID of a registered command (the plugin's own, or a built-in).</param>
/// <param name="Icon">Name of an icon from the host's icon set (for example <c>Sync</c>, <c>Add</c>, <c>Setting</c>); unknown names show no icon.</param>
/// <param name="ToolTip">Text shown on hover and read by screen readers.</param>
public sealed record PanelAction(string CommandId, string Icon, string ToolTip);

/// <summary>Fields shared by everything a plugin adds to the UI.</summary>
public abstract record DesktopContribution
{
    /// <summary>Stable ID. Must start with the plugin ID and a dot, so two plugins cannot collide.</summary>
    public required string Id { get; init; }

    /// <summary>Title in the default language.</summary>
    public required string Title { get; init; }

    /// <summary>Titles by culture name (<c>fr</c>, <c>pt-BR</c>); the host falls back to the parent culture, then <see cref="Title"/>.</summary>
    public IReadOnlyDictionary<string, string> LocalizedTitles { get; init; } = new Dictionary<string, string>();
}

/// <summary>A panel in the left or right side bar, with an activity-bar button and an optional toolbar.</summary>
public sealed record SidePanelContribution : DesktopContribution
{
    public PanelSlot Slot { get; init; } = PanelSlot.Right;

    /// <summary>Position in the activity bar; built-in panels use 10 to 30, so the default places plugin panels after them.</summary>
    public int Order { get; init; } = 100;

    public string Icon { get; init; } = "Document";

    public IReadOnlyList<PanelAction> Actions { get; init; } = [];

    /// <summary>Builds the panel's content, once, the first time it is shown.</summary>
    public required Func<IPluginWorkspace, Control> CreateContent { get; init; }
}

/// <summary>A group of settings shown under the plugin's entry on the Plugins page of Settings.</summary>
public sealed record SettingsPageContribution : DesktopContribution
{
    public string? Description { get; init; }

    /// <summary>Builds the settings content each time Settings opens.</summary>
    public required Func<IPluginWorkspace, Control> CreateContent { get; init; }
}

/// <summary>A section added to the inspector, below the built-in ones, while a key is selected.</summary>
public sealed record InspectorSectionContribution : DesktopContribution
{
    public int Order { get; init; } = 100;

    /// <summary>Builds the section's content, once. React to selection changes through <see cref="IPluginWorkspace.Changed"/>.</summary>
    public required Func<IPluginWorkspace, Control> CreateContent { get; init; }
}

/// <summary>A window the plugin can open with <see cref="IDesktopHost.ShowDialogAsync"/>.</summary>
public sealed record DialogContribution : DesktopContribution
{
    public double Width { get; init; } = 480;
    public double Height { get; init; }

    /// <summary>Builds the dialog's content each time it opens; the second argument is what the caller passed to <c>ShowDialogAsync</c>.</summary>
    public required Func<IPluginWorkspace, object?, Control> CreateContent { get; init; }
}

/// <summary>
/// A command offered for the selected key: as a button in the inspector's key actions and in the key context menus. The
/// command receives the key's name as its parameter.
/// </summary>
public sealed record EditorActionContribution : DesktopContribution
{
    /// <summary>ID of the command to run. Its own title, state and shortcut apply; <see cref="DesktopContribution.Title"/> only labels the action.</summary>
    public required string CommandId { get; init; }

    public int Order { get; init; } = 100;

    public string? Icon { get; init; }
}

/// <summary>Which end of the status bar an item sits at.</summary>
public enum StatusBarSide
{
    Left,
    Right,
}

/// <summary>How a badge is coloured.</summary>
public enum StatusBarSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// An item in the status bar. <see cref="DesktopContribution.Title"/> names it for screen readers and is its tooltip until the
/// plugin sets one; the initial text, icon and badge are what it shows at first.
/// </summary>
public sealed record StatusBarItemContribution : DesktopContribution
{
    public StatusBarSide Side { get; init; } = StatusBarSide.Left;

    /// <summary>Position among the items on the same side; built-in items use 10 to 50.</summary>
    public int Order { get; init; } = 100;

    public string? Text { get; init; }

    /// <summary>Name of an icon from the host's icon set.</summary>
    public string? Icon { get; init; }

    public string? Badge { get; init; }

    public StatusBarSeverity BadgeSeverity { get; init; } = StatusBarSeverity.Info;

    /// <summary>A command to run when the item is clicked. Without one the item is only informational.</summary>
    public string? CommandId { get; init; }
}
