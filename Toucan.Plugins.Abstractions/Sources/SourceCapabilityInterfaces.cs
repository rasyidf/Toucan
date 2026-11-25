namespace Toucan.Plugins.Sources;

/// <summary>Implemented by an adapter that can list the projects an account can see (capability <see cref="SourceCapabilities.ProjectDiscovery"/>).</summary>
public interface ISupportsProjectDiscovery
{
    IAsyncEnumerable<RemoteProjectSummary> ListProjectsAsync(ISourceAccountContext account, CancellationToken cancellationToken);
}

public sealed record RemoteProjectSummary(string Id, string Name, string? Description = null);

/// <summary>Implemented by a session that can read only what changed since a cursor (capability <see cref="SourceCapabilities.DeltaReads"/>).</summary>
public interface ISupportsDeltaReads
{
    IAsyncEnumerable<RemoteChangePage> ReadChangesAsync(DeltaReadRequest request, CancellationToken cancellationToken);
}

/// <param name="Cursor">From <see cref="RemoteUnitPage.Cursor"/> or the previous delta read.</param>
public sealed record DeltaReadRequest(string Cursor, IReadOnlyList<string>? ResourceIds = null, IReadOnlyList<string>? Languages = null);

/// <param name="Deleted">Units the service says were deleted. Language null means the whole key.</param>
/// <param name="AuthoritativeDeletes">The service guarantees <paramref name="Deleted"/> lists every deletion; otherwise the host confirms with a full read.</param>
public sealed record RemoteChangePage(IReadOnlyList<RemoteUnit> Changed, IReadOnlyList<RemoteDeletion> Deleted, string? ContinuationToken,
    bool IsComplete, string? Cursor, bool AuthoritativeDeletes = false);

public sealed record RemoteDeletion(RemoteKey Key, string? Language = null);

/// <summary>Implemented by a session whose writes or exports can finish later (capability <see cref="SourceCapabilities.AsyncJobs"/>).</summary>
public interface ISupportsAsyncJobs
{
    /// <summary>The job's state now. The host journals the handle and keeps polling after a restart.</summary>
    Task<SourceJobStatus> GetJobAsync(SourceJobHandle job, CancellationToken cancellationToken);
}

/// <param name="Id">The service's job ID. Kept in the host's journal, so it must survive a restart.</param>
public sealed record SourceJobHandle(string Id, TimeSpan? PollAfter = null);

public enum SourceJobState
{
    Running,
    Succeeded,
    Failed,
}

/// <param name="Results">Per-operation results once the job ends, matching the batch's operation IDs.</param>
public sealed record SourceJobStatus(SourceJobState State, IReadOnlyList<UnitOperationResult>? Results = null, SourceError? Error = null, double? Progress = null);

/// <summary>
/// Implemented by a session that can say when the service changed (webhook relay, long poll, server-sent events), capability
/// <see cref="SourceCapabilities.ChangeFeed"/>. Hints only trigger a read; they are never applied directly.
/// </summary>
public interface ISupportsChangeFeed
{
    IAsyncEnumerable<SourceChangeHint> WatchAsync(string? cursor, CancellationToken cancellationToken);
}

/// <param name="ResourceIds">What changed, when the service says; null means anything may have.</param>
public sealed record SourceChangeHint(string? Cursor, IReadOnlyList<string>? ResourceIds = null);

/// <summary>Implemented by a session that fetches heavy key context on demand, such as screenshots (capability <see cref="SourceCapabilities.UnitMetadata"/>).</summary>
public interface ISupportsUnitContext
{
    Task<SourceUnitMetadata?> GetContextAsync(RemoteKey key, string? language, CancellationToken cancellationToken);
}
