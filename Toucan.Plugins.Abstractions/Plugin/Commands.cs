namespace Toucan.Plugins;

/// <summary>Where a command may appear. The host decides how each placement looks.</summary>
[Flags]
public enum CommandPlacement
{
    None = 0,
    CommandPalette = 1,
    Menu = 2,
    Toolbar = 4,
    ContextMenu = 8,
}

/// <summary>Why a command can or cannot run right now. Anything but <see cref="Available"/> keeps it from running.</summary>
public enum CommandAvailability
{
    Available,

    /// <summary>Not shown anywhere (for example a feature switched off).</summary>
    Hidden,

    /// <summary>Shown but disabled; <see cref="CommandState.Reason"/> says why (no project open, nothing selected).</summary>
    Unavailable,

    /// <summary>Needs a connection that is not connected.</summary>
    Disconnected,

    /// <summary>Needs a licence the user does not have.</summary>
    Unlicensed,
}

public readonly record struct CommandState(CommandAvailability Availability, string? Reason = null)
{
    public static CommandState Available => new(CommandAvailability.Available);
    public bool CanRun => Availability == CommandAvailability.Available;
    public bool IsVisible => Availability != CommandAvailability.Hidden;
}

/// <summary>Static description of a command. The ID is the stable key for shortcuts, menus and scripts.</summary>
public sealed record CommandDefinition
{
    /// <summary>Stable ID: lowercase letters, digits, '.', '-'. A plugin's IDs must start with its plugin ID and a dot.</summary>
    public required string Id { get; init; }

    /// <summary>Display title in the default language.</summary>
    public required string Title { get; init; }

    /// <summary>Titles by culture name (<c>fr</c>, <c>pt-BR</c>); the host falls back to the parent culture, then <see cref="Title"/>.</summary>
    public IReadOnlyDictionary<string, string> LocalizedTitles { get; init; } = new Dictionary<string, string>();

    /// <summary>Group shown in the palette and the menu (for example "Sync").</summary>
    public required string Category { get; init; }

    public IReadOnlyDictionary<string, string> LocalizedCategories { get; init; } = new Dictionary<string, string>();

    public string? Description { get; init; }

    /// <summary>
    /// Default shortcut such as <c>Mod+Shift+K</c>. <c>Mod</c> is Cmd on macOS and Ctrl elsewhere; other modifiers are
    /// <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Meta</c>. Users can reassign or clear it.
    /// </summary>
    public string? DefaultShortcut { get; init; }

    public CommandPlacement Placements { get; init; } = CommandPlacement.CommandPalette | CommandPlacement.Menu;

    /// <summary>Not available while no project is open, and cancelled when the project closes.</summary>
    public bool RequiresWorkspace { get; init; } = true;

    /// <summary>Icon name from the host's icon set, if any.</summary>
    public string? Icon { get; init; }

    /// <summary>Which context menu a <see cref="CommandPlacement.ContextMenu"/> placement targets, e.g. <c>key</c> or <c>language</c>.</summary>
    public string? ContextMenuTarget { get; init; }
}

/// <summary>What a command can see about the application when deciding availability.</summary>
public interface ICommandContext
{
    bool HasWorkspace { get; }
    string? WorkspaceId { get; }
}

public sealed record CommandProgressInfo(string? Message = null, double? Fraction = null);

/// <summary>One execution of a command.</summary>
public interface ICommandInvocation
{
    ICommandContext Context { get; }

    /// <summary>Value supplied by the caller (a menu item parameter, a selected key), or null.</summary>
    object? Parameter { get; }

    /// <summary>Reports progress shown by the host. Safe to call from any thread.</summary>
    IProgress<CommandProgressInfo> Progress { get; }
}

public interface ICommandHandler
{
    /// <summary>Current state. Called often (menus, palette); keep it fast and free of I/O.</summary>
    CommandState GetState(ICommandContext context) => CommandState.Available;

    /// <summary>Runs the command. Honour <paramref name="cancellationToken"/>; it is cancelled by the user or when the project closes.</summary>
    Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken);
}
