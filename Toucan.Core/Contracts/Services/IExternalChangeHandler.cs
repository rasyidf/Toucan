namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Choices presented to the user when external file changes are detected
/// while the project has unsaved in-memory edits.
/// </summary>
public enum ExternalChangeChoice
{
    /// <summary>Reload all files from disk, discarding in-memory changes.</summary>
    Reload,

    /// <summary>Invoke the three-way merge engine to reconcile changes.</summary>
    Merge,

    /// <summary>Dismiss the notification and keep in-memory state unchanged.</summary>
    Ignore
}

/// <summary>
/// Abstraction for prompting the user about external file changes and showing
/// conflict resolution UI. The WPF layer implements this to show notifications
/// and diff dialogs. Nullable in the service constructor so tests can run without UI.
/// </summary>
public interface IExternalChangeHandler
{
    /// <summary>
    /// Prompts the user to choose how to handle external file changes when
    /// in-memory edits exist. Returns Reload, Merge, or Ignore.
    /// </summary>
    Task<ExternalChangeChoice> PromptAsync();

    /// <summary>
    /// Presents a conflict resolution dialog showing conflicting diff entries.
    /// Returns the user-resolved entries, or null if the user cancels.
    /// </summary>
    /// <param name="conflicts">The conflicting entries requiring manual resolution.</param>
    /// <returns>Resolved entries with the user's chosen values, or null if cancelled.</returns>
    Task<IReadOnlyList<DiffEntry>?> ShowConflictResolutionAsync(IReadOnlyList<DiffEntry> conflicts);
}
