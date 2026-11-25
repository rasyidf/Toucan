namespace Toucan.Plugins.Sources;

/// <summary>A session for a service that stores keys and values (a key/value REST API).</summary>
public interface IUnitSourceSession : IContentSourceSession
{
    /// <summary>
    /// Reads units page by page. The last page sets <see cref="RemoteUnitPage.IsComplete"/>. A read that ends without it is
    /// partial, and the host then infers no remote deletions from it. Throw <see cref="SourceException"/> for failures.
    /// </summary>
    IAsyncEnumerable<RemoteUnitPage> ReadUnitsAsync(UnitReadRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a batch and returns one result per operation, in order. Never throws for a refused or failed operation; report
    /// it in its result. When the request may or may not have reached the service, report <see cref="UnitOperationOutcome.Unknown"/>.
    /// </summary>
    Task<UnitWriteResult> WriteUnitsAsync(UnitWriteBatch batch, CancellationToken cancellationToken);
}

/// <param name="ResourceIds">Resources to read; null reads every resource of the connection.</param>
/// <param name="Languages">Remote language codes to read; null reads every mapped language.</param>
/// <param name="ContinuationToken">Resume after a page (the host uses this only to retry a failed page).</param>
public sealed record UnitReadRequest(IReadOnlyList<string>? ResourceIds = null, IReadOnlyList<string>? Languages = null, string? ContinuationToken = null);

/// <param name="ContinuationToken">Where the next page starts, or null on the last page.</param>
/// <param name="Cursor">A delta cursor for <see cref="ISupportsDeltaReads"/>, set on the last page when the service has one.</param>
public sealed record RemoteUnitPage(IReadOnlyList<RemoteUnit> Units, string? ContinuationToken, bool IsComplete, string? Cursor = null);

/// <summary>The identity of a remote key.</summary>
/// <param name="ResourceId">The resource holding it; use one fixed ID for services with a single namespace.</param>
/// <param name="Name">The key as the service shows it.</param>
/// <param name="RemoteId">The service's stable ID for the key when it has one; it survives renames.</param>
public sealed record RemoteKey(string ResourceId, string Name, string? RemoteId = null);

/// <summary>One translation as the service holds it.</summary>
public sealed record RemoteUnit(RemoteKey Key, string Language, string Value)
{
    /// <summary>Opaque version, compared only for equality (an ETag, a counter, a timestamp).</summary>
    public string? Revision { get; init; }
    public RemoteReviewStatus? Status { get; init; }
    public string? Comment { get; init; }

    /// <summary>Plural forms by CLDR category (<c>one</c>, <c>other</c>) when the service stores them apart.</summary>
    public IReadOnlyDictionary<string, string>? PluralForms { get; init; }
    public SourceUnitMetadata? Metadata { get; init; }
}

/// <summary>A remote review status and what it means in Toucan's terms. The raw value is kept so it can be written back.</summary>
public sealed record RemoteReviewStatus(string RemoteValue, SourceReviewMeaning Meaning);

public enum SourceReviewMeaning
{
    Unknown,
    Untranslated,
    Draft,
    NeedsReview,
    Reviewed,
    Approved,
    Rejected,
}

/// <summary>Context the service keeps for a key.</summary>
public sealed record SourceUnitMetadata(string? Description = null, int? MaxLength = null,
    IReadOnlyList<string>? Tags = null, IReadOnlyList<Uri>? Screenshots = null,
    IReadOnlyDictionary<string, string>? Extra = null);

/// <summary>
/// One write. <see cref="OperationId"/> is an idempotency key from the host's journal: a retry of the same operation
/// carries the same ID. Pass it to the service when it supports idempotency keys.
/// </summary>
public abstract record UnitWriteOperation(Guid OperationId);

/// <param name="ExpectedRevision">Write only if the unit still has this revision; null when the adapter has no conditional writes or the unit is new.</param>
public sealed record PutValue(Guid OperationId, RemoteKey Key, string Language, string Value, string? ExpectedRevision = null) : UnitWriteOperation(OperationId);

public sealed record PutStatus(Guid OperationId, RemoteKey Key, string Language, SourceReviewMeaning Meaning, string? ExpectedRevision = null) : UnitWriteOperation(OperationId);

public sealed record PutComment(Guid OperationId, RemoteKey Key, string Language, string Comment) : UnitWriteOperation(OperationId);

/// <param name="Values">Remote language code → value.</param>
public sealed record CreateRemoteKey(Guid OperationId, RemoteKey Key, IReadOnlyDictionary<string, string> Values, SourceUnitMetadata? Metadata = null) : UnitWriteOperation(OperationId);

public sealed record RenameRemoteKey(Guid OperationId, RemoteKey Key, string NewName) : UnitWriteOperation(OperationId);

public sealed record DeleteRemoteKey(Guid OperationId, RemoteKey Key) : UnitWriteOperation(OperationId);

public sealed record DeleteTranslation(Guid OperationId, RemoteKey Key, string Language, string? ExpectedRevision = null) : UnitWriteOperation(OperationId);

public sealed record UnitWriteBatch(IReadOnlyList<UnitWriteOperation> Operations);

/// <param name="Job">Set when the service finishes the batch later; results then say <see cref="UnitOperationOutcome.Pending"/>.</param>
public sealed record UnitWriteResult(IReadOnlyList<UnitOperationResult> Results, SourceJobHandle? Job = null);

public sealed record UnitOperationResult(Guid OperationId, UnitOperationOutcome Outcome)
{
    public string? NewRevision { get; init; }

    /// <summary>The service's ID for a key a <see cref="CreateRemoteKey"/> made.</summary>
    public string? AssignedRemoteId { get; init; }

    /// <summary>On <see cref="UnitOperationOutcome.Conflict"/>, the unit as the service holds it, when it says.</summary>
    public RemoteUnit? CurrentRemote { get; init; }
    public SourceError? Error { get; init; }
}

public enum UnitOperationOutcome
{
    /// <summary>The service did not say; treated like <see cref="Unknown"/>.</summary>
    None,

    Applied,

    /// <summary>The service recognised the idempotency key: this operation was applied earlier.</summary>
    AlreadyApplied,

    /// <summary>The expected revision no longer matches.</summary>
    Conflict,
    NotFound,

    /// <summary>The service refused it (validation, permission); retrying will not help.</summary>
    Refused,
    RateLimited,
    Failed,

    /// <summary>It may or may not have reached the service (a timeout after sending). The host reads before retrying.</summary>
    Unknown,

    /// <summary>Accepted for an asynchronous job; see <see cref="UnitWriteResult.Job"/>.</summary>
    Pending,
}
