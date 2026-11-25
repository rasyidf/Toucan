namespace Toucan.Plugins.Sources;

public enum SourceSyncKind
{
    Pull,
    Push,
    Sync,
}

public enum SourceSyncState
{
    Idle,
    Planning,
    Pulling,
    Pushing,
}

/// <summary>A connection's sync status as a plugin may read it. Holds no secrets and no sync state.</summary>
public sealed record SourceSyncStatus(string ConnectionId, string Name, SourceSyncState State, string Message, DateTimeOffset? LastPull,
    DateTimeOffset? LastPush, int PendingChanges, int Conflicts, bool RemoteChangesAvailable);

/// <summary>The size of a plan, without the content.</summary>
public sealed record SourceSyncPlanSummary(string ConnectionId, SourceSyncKind Kind, int PullChanges, int PushChanges, int Conflicts, int Notices,
    bool IsFirstSync, bool RemoteReadComplete);

/// <summary>What a run did.</summary>
public sealed record SourceSyncRunSummary(string ConnectionId, int Pulled, int Pushed, int Failed, int Conflicts, string? Message);

/// <summary>
/// Read sync state and request plans and runs for the open project's content-source connections (plugin API 1.3). A plugin
/// cannot write baselines or journals, resolve conflicts for the user, or see credentials. A run applies only what does not
/// conflict; conflicts stay for the user.
/// </summary>
public interface ISourceSyncApi
{
    IReadOnlyList<SourceSyncStatus> Connections { get; }

    SourceSyncStatus? Status(string connectionId);

    /// <summary>The newest plan summary for the connection, or null when none was made this session.</summary>
    SourceSyncPlanSummary? LastPlan(string connectionId);

    /// <summary>Where a translation stands with the sources it is bound to, or null when it is bound to none.</summary>
    SourceDeliveryState? DeliveryOf(Guid entryId, string language);

    /// <summary>Reads the service and plans; changes nothing.</summary>
    Task<SourceSyncPlanSummary> RequestPlanAsync(string connectionId, SourceSyncKind kind, CancellationToken cancellationToken = default);

    /// <summary>Plans and applies everything that does not conflict.</summary>
    Task<SourceSyncRunSummary> RunAsync(string connectionId, SourceSyncKind kind, CancellationToken cancellationToken = default);

    /// <summary>Raised when a status or a delivery state changed.</summary>
    event EventHandler? Changed;
}
