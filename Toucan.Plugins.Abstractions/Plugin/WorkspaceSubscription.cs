namespace Toucan.Plugins;

public enum WorkspaceChangeKind
{
    Updated,
    Saved,
    Reloaded,
    Undo,
    Redo,
    ConfigurationChanged,
    Closing,
    Closed,
    ResnapshotRequired,
}

public enum WorkspaceChangeOrigin
{
    Native,
    Plugin,
    History,
    Persistence,
    Source,
    Lifecycle,
    Configuration,
    Subscription,
}

/// <summary>A replacement unit, or null when the translation was removed.</summary>
public sealed record WorkspaceUnitChange(Guid EntryId, string Language, IdentifiedUnitSnapshot? Unit);

/// <summary>
/// An immutable delta. Regular changes continue PreviousRevision. ResnapshotRequired ends a slow
/// subscription; subscribe again for a fresh snapshot. Closed ends the session and can replace queued deltas.
/// </summary>
public sealed record WorkspaceChange(string WorkspaceId, Guid SessionId, long PreviousRevision, long Revision,
    WorkspaceChangeKind Kind, WorkspaceChangeOrigin Origin, string? OriginId,
    IReadOnlyList<WorkspaceUnitChange> Units, string PrimaryLanguage, IReadOnlyList<string> Languages,
    bool IsDirty, bool IsClosing);

/// <summary>
/// A snapshot and its gap-free continuation, scoped to one workspace session. Changes has one reader;
/// dispose the subscription when done. Cancellation and ending enumeration detach it from the engine.
/// </summary>
public interface IWorkspaceSubscription : IAsyncDisposable
{
    IdentifiedWorkspaceSnapshot InitialSnapshot { get; }
    IAsyncEnumerable<WorkspaceChange> Changes { get; }
}

/// <summary>Optional API 1.2 extension for independent, bounded workspace subscriptions.</summary>
public interface IWorkspaceSubscriptionApi : IIdentifiedWorkspaceApi
{
    /// <summary>Null when no workspace is open. Capacity must be between 1 and 4096 (default 128).</summary>
    Task<IWorkspaceSubscription?> SubscribeAsync(int capacity = 128, CancellationToken cancellationToken = default);
}
