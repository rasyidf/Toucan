using System.Security.Cryptography;
using Toucan.Plugins.Sources;

namespace Toucan.Plugins.Testing.Sources;

/// <summary>A change on a <see cref="FakeSourceServer"/>, as a change-feed hint.</summary>
public sealed class SourceChangedEventArgs(SourceChangeHint hint) : EventArgs
{
    public SourceChangeHint Hint { get; } = hint;
}

/// <summary>Which kind of call a <see cref="SourceFault"/> applies to.</summary>
public enum SourceCall
{
    Test,
    Describe,
    ReadUnits,
    WriteUnits,
    ListDocuments,
    ReadDocument,
    WriteDocument,
    DeleteDocument,
    ReadChanges,
    GetJob,
}

/// <summary>Something that goes wrong on the next matching call. Queue faults with <see cref="FakeSourceServer.Inject"/>.</summary>
public abstract record SourceFault(SourceCall Call);

/// <summary>429: the call fails with <see cref="SourceErrorKind.RateLimited"/> and a retry delay.</summary>
public sealed record RateLimitFault(SourceCall Call, TimeSpan RetryAfter) : SourceFault(Call);

/// <summary>5xx: the call fails with <see cref="SourceErrorKind.Unavailable"/>.</summary>
public sealed record UnavailableFault(SourceCall Call) : SourceFault(Call);

/// <summary>The credential is rejected once, as an expired token is.</summary>
public sealed record TokenExpiredFault(SourceCall Call) : SourceFault(Call);

/// <summary>
/// The connection drops. With <paramref name="AfterCommit"/> the server applied the write first, so the caller cannot know
/// whether it landed: the ambiguous failure the host must reconcile by reading.
/// </summary>
public sealed record TimeoutFault(SourceCall Call, bool AfterCommit) : SourceFault(Call);

/// <summary>A read stops after <paramref name="PagesBeforeDrop"/> pages without reaching the end. An honest adapter reports it incomplete.</summary>
public sealed record DroppedPageFault(int PagesBeforeDrop) : SourceFault(SourceCall.ReadUnits);

/// <summary>The page at <paramref name="PageIndex"/> is delivered twice (a retried request whose first answer also arrived).</summary>
public sealed record DuplicatePageFault(int PageIndex) : SourceFault(SourceCall.ReadUnits);

/// <summary>Another user changes the service just before the call is handled.</summary>
public sealed record ConcurrentEditFault(SourceCall Call, Action<FakeSourceServer> Edit) : SourceFault(Call);

/// <summary>The call waits first, honouring cancellation.</summary>
public sealed record LatencyFault(SourceCall Call, TimeSpan Delay) : SourceFault(Call);

/// <summary>
/// An in-memory translation service for tests: units with revisions, stable key IDs, review statuses and comments; documents
/// with versions; a change log for delta reads; idempotency keys; asynchronous jobs; and queued faults. Thread-safe.
/// Use it behind <see cref="FakeSourceAdapter"/>, or behind your own adapter's transport in tests.
/// </summary>
public sealed class FakeSourceServer
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Resource, string Key, string Language), RemoteUnit> _units = [];
    private readonly Dictionary<(string Resource, string Key), string> _keyIds = [];
    private readonly Dictionary<string, (byte[] Bytes, string Version)> _documents = new(StringComparer.Ordinal);
    private readonly List<(long Sequence, RemoteUnit? Changed, RemoteDeletion? Deleted)> _changes = [];
    private readonly Dictionary<Guid, UnitOperationResult> _applied = [];
    private readonly List<SourceFault> _faults = [];
    private readonly Dictionary<string, (int PollsLeft, IReadOnlyList<UnitOperationResult> Results)> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<SourceCall, int> _calls = [];
    private readonly List<UnitWriteOperation> _received = [];
    private long _revision;
    private long _sequence;
    private int _nextKeyId;
    private int _nextJob;

    public FakeSourceServer(string projectId = "fake-project", string projectName = "Fake project")
    {
        ProjectId = projectId;
        ProjectName = projectName;
    }

    public string ProjectId { get; }
    public string ProjectName { get; }

    /// <summary>Units per page in reads. Small by default so pagination is exercised.</summary>
    public int PageSize { get; set; } = 10;

    /// <summary>Languages the project has; units in other languages are refused.</summary>
    public IList<string> Languages { get; } = ["en", "fr", "de"];

    public string SourceLanguage { get; set; } = "en";

    public IList<RemoteResource> Resources { get; } = [new("main", "main")];

    /// <summary>What the credential may do; narrows the adapter's capabilities.</summary>
    public SourceCapabilities Permissions { get; set; } = (SourceCapabilities)int.MaxValue;

    /// <summary>The token the server accepts. Null accepts any.</summary>
    public string? AcceptedToken { get; set; }

    /// <summary>When true, writes return <see cref="UnitOperationOutcome.Pending"/> and finish after <see cref="JobPolls"/> polls.</summary>
    public bool UseAsyncJobs { get; set; }

    public int JobPolls { get; set; } = 2;

    /// <summary>Raised after any change, by a write or a seed: a change-feed hint.</summary>
    public event EventHandler<SourceChangedEventArgs>? Changed;

    /// <summary>How many times each kind of call reached the server, including failed ones.</summary>
    public int CallCount(SourceCall call) { lock (_gate) return _calls.GetValueOrDefault(call); }

    /// <summary>Every write operation the server received, in order, including repeats.</summary>
    public IReadOnlyList<UnitWriteOperation> ReceivedOperations { get { lock (_gate) return [.. _received]; } }

    /// <summary>Queues a fault for the next matching call. Faults run once, in order.</summary>
    public FakeSourceServer Inject(SourceFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        lock (_gate) _faults.Add(fault);
        return this;
    }

    public int PendingFaults { get { lock (_gate) return _faults.Count; } }

    // ───────────────────────────── seeding and inspection ─────────────────────────────

    /// <summary>Sets a unit as another user would, creating its key when needed. Returns the stored unit with its new revision.</summary>
    public RemoteUnit Put(string resourceId, string key, string language, string value, SourceReviewMeaning status = SourceReviewMeaning.Draft, string? comment = null)
    {
        RemoteUnit stored;
        lock (_gate)
        {
            var id = KeyIdLocked(resourceId, key);
            stored = new RemoteUnit(new RemoteKey(resourceId, key, id), language, value)
            {
                Revision = NextRevisionLocked(),
                Status = new RemoteReviewStatus(status.ToString().ToLowerInvariant(), status),
                Comment = comment,
            };
            _units[(resourceId, key, language)] = stored;
            LogLocked(stored, null);
        }
        RaiseChanged(resourceId);
        return stored;
    }

    /// <summary>Removes a key (every language) as another user would.</summary>
    public bool RemoveKey(string resourceId, string key)
    {
        bool removed;
        lock (_gate)
        {
            removed = RemoveKeyLocked(resourceId, key);
        }
        if (removed) RaiseChanged(resourceId);
        return removed;
    }

    public RemoteUnit? Get(string resourceId, string key, string language)
    {
        lock (_gate) return _units.GetValueOrDefault((resourceId, key, language));
    }

    public IReadOnlyList<RemoteUnit> Units { get { lock (_gate) return [.. Ordered()]; } }

    /// <summary>Sets a document's bytes as another user would. Returns its new version.</summary>
    public string PutDocument(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string version;
        lock (_gate)
        {
            version = VersionOf(bytes);
            _documents[path] = ([.. bytes], version);
        }
        RaiseChanged(null);
        return version;
    }

    public byte[]? GetDocument(string path) { lock (_gate) return _documents.TryGetValue(path, out var d) ? [.. d.Bytes] : null; }

    public IReadOnlyList<string> DocumentPaths { get { lock (_gate) return [.. _documents.Keys.Order(StringComparer.Ordinal)]; } }

    // ───────────────────────────── the service API, as an adapter calls it ─────────────────────────────

    public async Task<SourceTestResult> TestAsync(string? token, CancellationToken cancellationToken)
    {
        try
        {
            await EnterAsync(SourceCall.Test, token, cancellationToken).ConfigureAwait(false);
            return SourceTestResult.Ok($"Connected to {ProjectName}.");
        }
        catch (SourceException ex)
        {
            return SourceTestResult.Failed(ex.Error);
        }
    }

    public async Task<SourceProjectInfo> DescribeAsync(string? token, CancellationToken cancellationToken)
    {
        await EnterAsync(SourceCall.Describe, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            return new SourceProjectInfo(ProjectId, ProjectName, SourceLanguage,
                [.. Languages.Select(l => new RemoteLanguage(l, null, l == SourceLanguage))], [.. Resources], Permissions,
                new SourceLimits(MaxBatchSize: 50, MaxConcurrency: 4));
        }
    }

    /// <summary>One page of units, starting at the request's continuation token (an index).</summary>
    public async Task<RemoteUnitPage> ReadPageAsync(string? token, UnitReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnterAsync(SourceCall.ReadUnits, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var all = Ordered().Where(u => (request.ResourceIds is null || request.ResourceIds.Contains(u.Key.ResourceId))
                                           && (request.Languages is null || request.Languages.Contains(u.Language))).ToList();
            var start = int.TryParse(request.ContinuationToken, out var s) ? s : 0;
            var page = all.Skip(start).Take(PageSize).ToList();
            var next = start + page.Count;
            var complete = next >= all.Count;
            return new RemoteUnitPage(page, complete ? null : next.ToString(System.Globalization.CultureInfo.InvariantCulture), complete,
                complete ? _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }
    }

    /// <summary>The read faults for a whole read (dropped and duplicated pages), taken once per read.</summary>
    public (int? DropAfter, int? Duplicate) TakeReadShape()
    {
        lock (_gate)
        {
            int? drop = null, duplicate = null;
            if (_faults.OfType<DroppedPageFault>().FirstOrDefault() is { } d) { _faults.Remove(d); drop = d.PagesBeforeDrop; }
            if (_faults.OfType<DuplicatePageFault>().FirstOrDefault() is { } p) { _faults.Remove(p); duplicate = p.PageIndex; }
            return (drop, duplicate);
        }
    }

    public async Task<UnitWriteResult> WriteAsync(string? token, UnitWriteBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var timeout = TakeFault<TimeoutFault>(SourceCall.WriteUnits);
        if (timeout is { AfterCommit: false }) { Count(SourceCall.WriteUnits); throw new SourceException(new SourceError(SourceErrorKind.Timeout, "The request timed out.")); }
        await EnterAsync(SourceCall.WriteUnits, token, cancellationToken).ConfigureAwait(false);

        List<UnitOperationResult> results;
        var touched = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            results = [.. batch.Operations.Select(op => ApplyLocked(op, touched))];
        }
        foreach (var resource in touched) RaiseChanged(resource);

        if (timeout is { AfterCommit: true })
            throw new SourceException(new SourceError(SourceErrorKind.Timeout, "The connection dropped after the request was sent."));
        if (!UseAsyncJobs) return new UnitWriteResult(results);

        lock (_gate)
        {
            var id = $"job-{++_nextJob}";
            _jobs[id] = (JobPolls, results);
            return new UnitWriteResult([.. results.Select(r => new UnitOperationResult(r.OperationId, UnitOperationOutcome.Pending))], new SourceJobHandle(id, TimeSpan.Zero));
        }
    }

    public async Task<SourceJobStatus> GetJobAsync(string? token, SourceJobHandle job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await EnterAsync(SourceCall.GetJob, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_jobs.TryGetValue(job.Id, out var state))
                return new SourceJobStatus(SourceJobState.Failed, Error: new SourceError(SourceErrorKind.NotFound, $"No job {job.Id}."));
            if (state.PollsLeft > 0)
            {
                _jobs[job.Id] = (state.PollsLeft - 1, state.Results);
                return new SourceJobStatus(SourceJobState.Running, Progress: 1.0 - (double)state.PollsLeft / Math.Max(1, JobPolls + 1));
            }
            return new SourceJobStatus(SourceJobState.Succeeded, state.Results, Progress: 1);
        }
    }

    public async Task<RemoteChangePage> ReadChangesAsync(string? token, DeltaReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnterAsync(SourceCall.ReadChanges, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (!long.TryParse(request.Cursor, out var since) || since > _sequence)
                throw new SourceException(new SourceError(SourceErrorKind.Validation, "Unknown cursor; read everything again."));
            var entries = _changes.Where(c => c.Sequence > since).ToList();
            var changed = entries.Where(c => c.Changed is not null).Select(c => c.Changed!)
                .GroupBy(u => (u.Key.ResourceId, u.Key.Name, u.Language)).Select(g => g.Last())
                .Where(u => _units.TryGetValue((u.Key.ResourceId, u.Key.Name, u.Language), out var now) && now.Revision == u.Revision).ToList();
            var deleted = entries.Where(c => c.Deleted is not null).Select(c => c.Deleted!).Distinct().ToList();
            return new RemoteChangePage(changed, deleted, null, true, _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), AuthoritativeDeletes: true);
        }
    }

    public async Task<RemoteDocumentPage> ListDocumentsAsync(string? token, DocumentListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnterAsync(SourceCall.ListDocuments, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            var all = _documents.Where(d => request.Prefix is null || d.Key.StartsWith(request.Prefix, StringComparison.Ordinal))
                .OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
            var start = int.TryParse(request.ContinuationToken, out var s) ? s : 0;
            var page = all.Skip(start).Take(PageSize).ToList();
            var next = start + page.Count;
            var complete = next >= all.Count;
            return new RemoteDocumentPage([.. page.Select(d => new RemoteDocumentInfo(new RemoteDocumentRef(d.Key), d.Value.Version, d.Value.Bytes.Length))],
                complete ? null : next.ToString(System.Globalization.CultureInfo.InvariantCulture), complete);
        }
    }

    public async Task<RemoteDocumentContent> ReadDocumentAsync(string? token, RemoteDocumentRef document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnterAsync(SourceCall.ReadDocument, token, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_documents.TryGetValue(document.Path, out var d))
                throw new SourceException(new SourceError(SourceErrorKind.NotFound, $"No document {document.Path}."));
            return new RemoteDocumentContent(document, d.Bytes.ToArray(), d.Version);
        }
    }

    public async Task<DocumentWriteResult> WriteDocumentAsync(string? token, RemoteDocumentWrite write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        var timeout = TakeFault<TimeoutFault>(SourceCall.WriteDocument);
        if (timeout is { AfterCommit: false }) { Count(SourceCall.WriteDocument); throw new SourceException(new SourceError(SourceErrorKind.Timeout, "The request timed out.")); }
        await EnterAsync(SourceCall.WriteDocument, token, cancellationToken).ConfigureAwait(false);
        DocumentWriteResult result;
        lock (_gate)
        {
            var exists = _documents.TryGetValue(write.Ref.Path, out var current);
            if (_applied.ContainsKey(write.OperationId))
                result = new DocumentWriteResult(UnitOperationOutcome.AlreadyApplied, exists ? current.Version : null);
            else if (write.ExpectedVersion is null ? exists : !exists || current.Version != write.ExpectedVersion)
                result = new DocumentWriteResult(UnitOperationOutcome.Conflict, exists ? current.Version : null,
                    new SourceError(SourceErrorKind.Conflict, $"{write.Ref.Path} changed on the server."));
            else
            {
                var bytes = write.Bytes.ToArray();
                var version = VersionOf(bytes);
                _documents[write.Ref.Path] = (bytes, version);
                _applied[write.OperationId] = new UnitOperationResult(write.OperationId, UnitOperationOutcome.Applied) { NewRevision = version };
                result = new DocumentWriteResult(UnitOperationOutcome.Applied, version);
            }
        }
        if (result.Outcome == UnitOperationOutcome.Applied) RaiseChanged(null);
        if (timeout is { AfterCommit: true })
            throw new SourceException(new SourceError(SourceErrorKind.Timeout, "The connection dropped after the request was sent."));
        return result;
    }

    public async Task<DocumentWriteResult> DeleteDocumentAsync(string? token, RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnterAsync(SourceCall.DeleteDocument, token, cancellationToken).ConfigureAwait(false);
        DocumentWriteResult result;
        lock (_gate)
        {
            if (_applied.ContainsKey(operationId)) result = new DocumentWriteResult(UnitOperationOutcome.AlreadyApplied);
            else if (!_documents.TryGetValue(document.Path, out var current)) result = new DocumentWriteResult(UnitOperationOutcome.NotFound);
            else if (current.Version != expectedVersion)
                result = new DocumentWriteResult(UnitOperationOutcome.Conflict, current.Version, new SourceError(SourceErrorKind.Conflict, $"{document.Path} changed on the server."));
            else
            {
                _documents.Remove(document.Path);
                _applied[operationId] = new UnitOperationResult(operationId, UnitOperationOutcome.Applied);
                result = new DocumentWriteResult(UnitOperationOutcome.Applied);
            }
        }
        if (result.Outcome == UnitOperationOutcome.Applied) RaiseChanged(null);
        return result;
    }

    // ───────────────────────────── internals ─────────────────────────────

    private async Task EnterAsync(SourceCall call, string? token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count(call);
        while (TakeFault(call) is { } fault)
        {
            switch (fault)
            {
                case LatencyFault latency:
                    await Task.Delay(latency.Delay, cancellationToken).ConfigureAwait(false);
                    break;
                case ConcurrentEditFault edit:
                    edit.Edit(this);
                    break;
                case RateLimitFault rate:
                    throw new SourceException(new SourceError(SourceErrorKind.RateLimited, "Too many requests.") { RetryAfter = rate.RetryAfter });
                case UnavailableFault:
                    throw new SourceException(new SourceError(SourceErrorKind.Unavailable, "The service is unavailable."));
                case TokenExpiredFault:
                    throw new SourceException(new SourceError(SourceErrorKind.Authentication, "The token has expired."));
            }
        }
        if (AcceptedToken is not null && token != AcceptedToken)
            throw new SourceException(new SourceError(SourceErrorKind.Authentication, "The token is not valid."));
    }

    private SourceFault? TakeFault(SourceCall call)
    {
        lock (_gate)
        {
            var fault = _faults.FirstOrDefault(f => f.Call == call && f is not TimeoutFault and not DroppedPageFault and not DuplicatePageFault);
            if (fault is not null) _faults.Remove(fault);
            return fault;
        }
    }

    private T? TakeFault<T>(SourceCall call) where T : SourceFault
    {
        lock (_gate)
        {
            var fault = _faults.OfType<T>().FirstOrDefault(f => f.Call == call);
            if (fault is not null) _faults.Remove(fault);
            return fault;
        }
    }

    private void Count(SourceCall call)
    {
        lock (_gate) _calls[call] = _calls.GetValueOrDefault(call) + 1;
    }

    private UnitOperationResult ApplyLocked(UnitWriteOperation op, HashSet<string> touched)
    {
        _received.Add(op);
        if (_applied.TryGetValue(op.OperationId, out var earlier))
            return earlier with { Outcome = UnitOperationOutcome.AlreadyApplied };

        var result = op switch
        {
            PutValue put => PutValueLocked(put),
            PutStatus status => PutStatusLocked(status),
            PutComment comment => PutCommentLocked(comment),
            CreateRemoteKey create => CreateLocked(create),
            RenameRemoteKey rename => RenameLocked(rename),
            DeleteRemoteKey delete => RemoveKeyLocked(delete.Key.ResourceId, delete.Key.Name)
                ? new UnitOperationResult(op.OperationId, UnitOperationOutcome.Applied)
                : new UnitOperationResult(op.OperationId, UnitOperationOutcome.NotFound),
            DeleteTranslation delete => DeleteTranslationLocked(delete),
            _ => new UnitOperationResult(op.OperationId, UnitOperationOutcome.Refused) { Error = new SourceError(SourceErrorKind.Unsupported, $"{op.GetType().Name} is not supported.") },
        };
        if (result.Outcome == UnitOperationOutcome.Applied)
        {
            _applied[op.OperationId] = result;
            touched.Add(ResourceOf(op));
        }
        return result;
    }

    private static string ResourceOf(UnitWriteOperation op) => op switch
    {
        PutValue p => p.Key.ResourceId,
        PutStatus p => p.Key.ResourceId,
        PutComment p => p.Key.ResourceId,
        CreateRemoteKey p => p.Key.ResourceId,
        RenameRemoteKey p => p.Key.ResourceId,
        DeleteRemoteKey p => p.Key.ResourceId,
        DeleteTranslation p => p.Key.ResourceId,
        _ => string.Empty,
    };

    private UnitOperationResult PutValueLocked(PutValue put)
    {
        if (!Languages.Contains(put.Language)) return Refused(put.OperationId, $"Language {put.Language} is not in the project.");
        var slot = (put.Key.ResourceId, put.Key.Name, put.Language);
        var exists = _units.TryGetValue(slot, out var current);
        if (!exists && !_keyIds.ContainsKey((put.Key.ResourceId, put.Key.Name)))
            return new UnitOperationResult(put.OperationId, UnitOperationOutcome.NotFound) { Error = new SourceError(SourceErrorKind.NotFound, $"No key {put.Key.Name}.") };
        if (put.ExpectedRevision is not null && (!exists || current!.Revision != put.ExpectedRevision))
            return new UnitOperationResult(put.OperationId, UnitOperationOutcome.Conflict) { CurrentRemote = current, Error = new SourceError(SourceErrorKind.Conflict, "Changed on the server.") };
        var stored = (current ?? new RemoteUnit(new RemoteKey(put.Key.ResourceId, put.Key.Name, _keyIds[(put.Key.ResourceId, put.Key.Name)]), put.Language, string.Empty))
            with { Value = put.Value, Revision = NextRevisionLocked() };
        _units[slot] = stored;
        LogLocked(stored, null);
        return new UnitOperationResult(put.OperationId, UnitOperationOutcome.Applied) { NewRevision = stored.Revision };
    }

    private UnitOperationResult PutStatusLocked(PutStatus put)
    {
        if (!_units.TryGetValue((put.Key.ResourceId, put.Key.Name, put.Language), out var current))
            return new UnitOperationResult(put.OperationId, UnitOperationOutcome.NotFound);
        if (put.ExpectedRevision is not null && current.Revision != put.ExpectedRevision)
            return new UnitOperationResult(put.OperationId, UnitOperationOutcome.Conflict) { CurrentRemote = current };
        var stored = current with { Status = new RemoteReviewStatus(put.Meaning.ToString().ToLowerInvariant(), put.Meaning), Revision = NextRevisionLocked() };
        _units[(put.Key.ResourceId, put.Key.Name, put.Language)] = stored;
        LogLocked(stored, null);
        return new UnitOperationResult(put.OperationId, UnitOperationOutcome.Applied) { NewRevision = stored.Revision };
    }

    private UnitOperationResult PutCommentLocked(PutComment put)
    {
        if (!_units.TryGetValue((put.Key.ResourceId, put.Key.Name, put.Language), out var current))
            return new UnitOperationResult(put.OperationId, UnitOperationOutcome.NotFound);
        var stored = current with { Comment = put.Comment, Revision = NextRevisionLocked() };
        _units[(put.Key.ResourceId, put.Key.Name, put.Language)] = stored;
        LogLocked(stored, null);
        return new UnitOperationResult(put.OperationId, UnitOperationOutcome.Applied) { NewRevision = stored.Revision };
    }

    private UnitOperationResult CreateLocked(CreateRemoteKey create)
    {
        if (_keyIds.ContainsKey((create.Key.ResourceId, create.Key.Name)))
            return new UnitOperationResult(create.OperationId, UnitOperationOutcome.Conflict) { Error = new SourceError(SourceErrorKind.Conflict, $"Key {create.Key.Name} exists.") };
        if (create.Values.Keys.FirstOrDefault(l => !Languages.Contains(l)) is { } unknown) return Refused(create.OperationId, $"Language {unknown} is not in the project.");
        var id = KeyIdLocked(create.Key.ResourceId, create.Key.Name);
        foreach (var (language, value) in create.Values)
        {
            var unit = new RemoteUnit(new RemoteKey(create.Key.ResourceId, create.Key.Name, id), language, value) { Revision = NextRevisionLocked(), Metadata = create.Metadata };
            _units[(create.Key.ResourceId, create.Key.Name, language)] = unit;
            LogLocked(unit, null);
        }
        return new UnitOperationResult(create.OperationId, UnitOperationOutcome.Applied) { AssignedRemoteId = id };
    }

    private UnitOperationResult RenameLocked(RenameRemoteKey rename)
    {
        var from = (rename.Key.ResourceId, rename.Key.Name);
        if (!_keyIds.TryGetValue(from, out var id)) return new UnitOperationResult(rename.OperationId, UnitOperationOutcome.NotFound);
        if (_keyIds.ContainsKey((rename.Key.ResourceId, rename.NewName)))
            return new UnitOperationResult(rename.OperationId, UnitOperationOutcome.Conflict) { Error = new SourceError(SourceErrorKind.Conflict, $"Key {rename.NewName} exists.") };
        _keyIds.Remove(from);
        _keyIds[(rename.Key.ResourceId, rename.NewName)] = id;
        foreach (var slot in _units.Keys.Where(k => k.Resource == rename.Key.ResourceId && k.Key == rename.Key.Name).ToList())
        {
            var moved = _units[slot] with { Key = new RemoteKey(rename.Key.ResourceId, rename.NewName, id), Revision = NextRevisionLocked() };
            _units.Remove(slot);
            _units[(rename.Key.ResourceId, rename.NewName, slot.Language)] = moved;
            LogLocked(null, new RemoteDeletion(new RemoteKey(rename.Key.ResourceId, rename.Key.Name, id), slot.Language));
            LogLocked(moved, null);
        }
        return new UnitOperationResult(rename.OperationId, UnitOperationOutcome.Applied);
    }

    private UnitOperationResult DeleteTranslationLocked(DeleteTranslation delete)
    {
        var slot = (delete.Key.ResourceId, delete.Key.Name, delete.Language);
        if (!_units.TryGetValue(slot, out var current)) return new UnitOperationResult(delete.OperationId, UnitOperationOutcome.NotFound);
        if (delete.ExpectedRevision is not null && current.Revision != delete.ExpectedRevision)
            return new UnitOperationResult(delete.OperationId, UnitOperationOutcome.Conflict) { CurrentRemote = current };
        _units.Remove(slot);
        LogLocked(null, new RemoteDeletion(current.Key, delete.Language));
        return new UnitOperationResult(delete.OperationId, UnitOperationOutcome.Applied);
    }

    private bool RemoveKeyLocked(string resourceId, string key)
    {
        if (!_keyIds.Remove((resourceId, key), out var id)) return false;
        foreach (var slot in _units.Keys.Where(k => k.Resource == resourceId && k.Key == key).ToList()) _units.Remove(slot);
        LogLocked(null, new RemoteDeletion(new RemoteKey(resourceId, key, id)));
        return true;
    }

    private static UnitOperationResult Refused(Guid id, string message) =>
        new(id, UnitOperationOutcome.Refused) { Error = new SourceError(SourceErrorKind.Validation, message) };

    private string KeyIdLocked(string resourceId, string key)
    {
        if (!_keyIds.TryGetValue((resourceId, key), out var id))
            _keyIds[(resourceId, key)] = id = $"k{++_nextKeyId}";
        return id;
    }

    private string NextRevisionLocked() => (++_revision).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void LogLocked(RemoteUnit? changed, RemoteDeletion? deleted) => _changes.Add((++_sequence, changed, deleted));

    private IEnumerable<RemoteUnit> Ordered() =>
        _units.Values.OrderBy(u => u.Key.ResourceId, StringComparer.Ordinal).ThenBy(u => u.Key.Name, StringComparer.Ordinal).ThenBy(u => u.Language, StringComparer.Ordinal);

    private void RaiseChanged(string? resourceId)
    {
        string cursor;
        lock (_gate) cursor = _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Changed?.Invoke(this, new SourceChangedEventArgs(new SourceChangeHint(cursor, resourceId is null ? null : [resourceId])));
    }

    private static string VersionOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
}
