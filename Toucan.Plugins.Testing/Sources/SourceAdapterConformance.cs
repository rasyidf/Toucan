using System.Text;
using Toucan.Plugins.Sources;

namespace Toucan.Plugins.Testing.Sources;

/// <summary>
/// The remote project the conformance suite works in: a sandbox project, a recorded server, or <see cref="FakeServerFixture"/>.
/// Each check starts with <see cref="ResetAsync"/>, so use a project with nothing else in it.
/// </summary>
public interface ISourceConformanceFixture
{
    /// <summary>A session context for the test project, with a working credential.</summary>
    ISourceSessionContext CreateContext();

    /// <summary>The resource the suite writes in.</summary>
    string ResourceId { get; }

    /// <summary>A remote language code the suite may write.</summary>
    string Language { get; }

    /// <summary>How many units to seed so that a read spans several pages.</summary>
    int UnitsForPagination { get; }

    /// <summary>Empties the project.</summary>
    Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>Creates keys with values in <see cref="Language"/>, as another user would.</summary>
    Task SeedAsync(IReadOnlyList<(string Key, string Value)> units, CancellationToken cancellationToken);

    /// <summary>Changes a value as another user would, outside the adapter.</summary>
    Task EditRemotelyAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>The value the service holds now, or null when the unit does not exist.</summary>
    Task<string?> ValueOfAsync(string key, CancellationToken cancellationToken);

    /// <summary>How many keys with this name the service holds (more than one means a duplicate write).</summary>
    Task<int> CountKeysAsync(string key, CancellationToken cancellationToken);

    /// <summary>Fault injection, or null when the fixture cannot inject faults (fault checks are then skipped).</summary>
    ISourceFaultInjector? Faults { get; }
}

/// <summary>Faults a fixture can make the service produce on the next matching call.</summary>
public interface ISourceFaultInjector
{
    void DropReadAfter(int pages);
    void ExpireTokenOnce(SourceCall target);
    void RateLimitOnce(SourceCall target, TimeSpan retryAfter);

    /// <summary>The next write lands, then the connection drops before the answer arrives.</summary>
    void TimeoutAfterCommitOnce(SourceCall target);
}

/// <summary>Optional: a fixture that can seed documents, for adapters with <see cref="SourceCapabilities.ReadDocuments"/>.</summary>
public interface IDocumentConformanceFixture
{
    Task SeedDocumentAsync(string path, byte[] bytes, CancellationToken cancellationToken);
    Task<byte[]?> DocumentAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Optional: a fixture that sees every request the adapter makes, to check that all of it goes through the host's HTTP client.</summary>
public interface ISourceTrafficObserver
{
    /// <summary>Requests the service received that did not come through the context's <see cref="ISourceAccountContext.Http"/>.</summary>
    int UnroutedRequests { get; }
}

public enum ConformanceOutcome
{
    Passed,
    Failed,

    /// <summary>Not applicable to this adapter or fixture; the message says why.</summary>
    Skipped,
}

public sealed record ConformanceResult(string Check, ConformanceOutcome Outcome, string Message);

/// <summary>What <see cref="SourceAdapterConformance.RunAsync"/> found.</summary>
public sealed class ConformanceReport(IReadOnlyList<ConformanceResult> results)
{
    public IReadOnlyList<ConformanceResult> Results { get; } = results;
    public IReadOnlyList<ConformanceResult> Failures => [.. Results.Where(r => r.Outcome == ConformanceOutcome.Failed)];
    public bool Passed => Failures.Count == 0;

    public ConformanceResult this[string check] => Results.Single(r => r.Check == check);

    /// <summary>Throws with every failure listed, for use in any test framework.</summary>
    public void ThrowIfFailed()
    {
        if (!Passed) throw new SourceConformanceException(ToString());
    }

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var r in Results) text.Append(r.Outcome switch { ConformanceOutcome.Passed => "PASS ", ConformanceOutcome.Failed => "FAIL ", _ => "SKIP " }).Append(r.Check).Append(": ").AppendLine(r.Message);
        return text.ToString();
    }
}

public sealed class SourceConformanceException : Exception
{
    public SourceConformanceException() { }
    public SourceConformanceException(string message) : base(message) { }
    public SourceConformanceException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Checks that an adapter keeps the promises the host relies on: honest pagination and completeness, per-operation results
/// in order, conflict detection, ambiguous failures reported as Unknown, error kinds the host can act on, cancellation,
/// and the registration rules. Run it against a sandbox project or a recorded server in your adapter's tests:
/// <code>(await SourceAdapterConformance.RunAsync(adapter, manifest, fixture)).ThrowIfFailed();</code>
/// </summary>
public static class SourceAdapterConformance
{
    public const string Descriptor = "descriptor";
    public const string SessionInterfaces = "session-interfaces";
    public const string TestConnection = "test-connection";
    public const string Describe = "describe";
    public const string ReadPagination = "read-pagination";
    public const string ReadCompleteness = "read-incomplete-is-reported";
    public const string ReadCancellation = "read-cancellation";
    public const string WriteResults = "write-results";
    public const string WriteIdempotency = "write-idempotency";
    public const string CreateIdempotency = "create-no-duplicates";
    public const string ConflictDetection = "conflict-detection";
    public const string AmbiguousFailure = "ambiguous-failure";
    public const string ExpiredToken = "expired-token";
    public const string RateLimit = "rate-limit";
    public const string WriteCancellation = "write-cancellation";
    public const string Documents = "documents";
    public const string DocumentConflicts = "document-conflicts";
    public const string Traffic = "traffic-through-host";
    public const string Dispose = "dispose";

    private const int MaxJobPolls = 100;

    public static async Task<ConformanceReport> RunAsync(IContentSourceAdapter adapter, PluginManifest manifest, ISourceConformanceFixture fixture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(fixture);
        var results = new List<ConformanceResult>();
        var caps = adapter.Descriptor?.Capabilities ?? SourceCapabilities.None;
        var run = new Runner(adapter, fixture, results, cancellationToken);

        var problems = SourceAdapterRules.Check(adapter, manifest);
        results.Add(problems.Count == 0 ? Pass(Descriptor, "The descriptor meets the registration rules.") : Fail(Descriptor, string.Join(" ", problems)));
        if (adapter.Descriptor is null) return new ConformanceReport(results);

        await run.CheckAsync(SessionInterfaces, true, "", session => CheckInterfaces(session, caps)).ConfigureAwait(false);
        await run.CheckAsync(TestConnection, true, "", async session =>
        {
            var test = await session.TestAsync(cancellationToken).ConfigureAwait(false);
            return test.Succeeded ? Pass(TestConnection, test.Message) : Fail(TestConnection, $"TestAsync failed: {test.Message}");
        }).ConfigureAwait(false);
        await run.CheckAsync(Describe, true, "", async session =>
        {
            var info = await session.DescribeAsync(cancellationToken).ConfigureAwait(false);
            var expected = fixture.CreateContext().Connection.RemoteProjectId;
            return info.RemoteProjectId == expected ? Pass(Describe, $"Describes '{info.Name}'.")
                : Fail(Describe, $"DescribeAsync returned project '{info.RemoteProjectId}', expected '{expected}'.");
        }).ConfigureAwait(false);

        var units = caps.HasFlag(SourceCapabilities.ReadUnits);
        const string noUnits = "The adapter does not read units.";
        await run.CheckAsync(ReadPagination, units, noUnits, s => CheckPaginationAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(ReadCompleteness, units && fixture.Faults is not null, units ? "The fixture cannot inject faults." : noUnits,
            s => CheckDroppedReadAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(ReadCancellation, units, noUnits, s => CheckReadCancellationAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);

        var writes = units && caps.HasFlag(SourceCapabilities.WriteValues);
        const string noWrites = "The adapter does not write values.";
        await run.CheckAsync(WriteResults, writes, noWrites, s => CheckWriteResultsAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(WriteIdempotency, writes, noWrites, s => CheckWriteIdempotencyAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(CreateIdempotency, units && caps.HasFlag(SourceCapabilities.CreateKeys), "The adapter does not create keys.",
            s => CheckCreateIdempotencyAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(ConflictDetection, writes && caps.HasFlag(SourceCapabilities.ConditionalWrites), "The adapter has no conditional writes.",
            s => CheckConflictAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(AmbiguousFailure, writes && fixture.Faults is not null, writes ? "The fixture cannot inject faults." : noWrites,
            s => CheckAmbiguousAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(ExpiredToken, units && fixture.Faults is not null, units ? "The fixture cannot inject faults." : noUnits,
            s => CheckExpiredTokenAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(RateLimit, units && fixture.Faults is not null, units ? "The fixture cannot inject faults." : noUnits,
            s => CheckRateLimitAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(WriteCancellation, writes, noWrites, s => CheckWriteCancellationAsync((IUnitSourceSession)s, fixture, cancellationToken)).ConfigureAwait(false);

        var documents = caps.HasFlag(SourceCapabilities.ReadDocuments);
        var docFixture = fixture as IDocumentConformanceFixture;
        await run.CheckAsync(Documents, documents && docFixture is not null, documents ? "The fixture cannot seed documents." : "The adapter does not read documents.",
            s => CheckDocumentsAsync((IDocumentSourceSession)s, docFixture!, cancellationToken)).ConfigureAwait(false);
        await run.CheckAsync(DocumentConflicts, documents && caps.HasFlag(SourceCapabilities.WriteDocuments) && docFixture is not null,
            "The adapter does not write documents, or the fixture cannot seed them.",
            s => CheckDocumentConflictsAsync((IDocumentSourceSession)s, docFixture!, cancellationToken)).ConfigureAwait(false);

        results.Add(fixture is ISourceTrafficObserver observer
            ? observer.UnroutedRequests == 0 ? Pass(Traffic, "Every request went through the host's HTTP client.")
                : Fail(Traffic, $"{observer.UnroutedRequests} requests bypassed ISourceSessionContext.Http.")
            : Skip(Traffic, "The fixture does not observe traffic."));

        await run.CheckAsync(Dispose, true, "", async session =>
        {
            await session.DisposeAsync().ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            return Pass(Dispose, "Disposing twice is safe.");
        }).ConfigureAwait(false);

        return new ConformanceReport(results);
    }

    private static ConformanceResult Pass(string check, string message) => new(check, ConformanceOutcome.Passed, message);
    private static ConformanceResult Fail(string check, string message) => new(check, ConformanceOutcome.Failed, message);
    private static ConformanceResult Skip(string check, string message) => new(check, ConformanceOutcome.Skipped, message);

    private sealed class Runner(IContentSourceAdapter adapter, ISourceConformanceFixture fixture, List<ConformanceResult> results, CancellationToken cancellationToken)
    {
        public async Task CheckAsync(string check, bool applies, string skipReason, Func<IContentSourceSession, Task<ConformanceResult>> body)
        {
            if (!applies) { results.Add(Skip(check, skipReason)); return; }
            IContentSourceSession? session = null;
            try
            {
                await fixture.ResetAsync(cancellationToken).ConfigureAwait(false);
                session = await adapter.OpenSessionAsync(fixture.CreateContext(), cancellationToken).ConfigureAwait(false);
                results.Add(await body(session).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                results.Add(Fail(check, $"{ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                if (session is not null)
                {
                    try { await session.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) when (check != Dispose) { results.Add(Fail(Dispose, $"DisposeAsync threw {ex.GetType().Name}: {ex.Message}")); }
                }
            }
        }
    }

    private static Task<ConformanceResult> CheckInterfaces(IContentSourceSession session, SourceCapabilities caps)
    {
        var missing = new List<string>();
        void Need(SourceCapabilities flag, bool has, string name) { if (caps.HasFlag(flag) && !has) missing.Add($"{flag} needs {name}"); }
        Need(SourceCapabilities.ReadUnits, session is IUnitSourceSession, nameof(IUnitSourceSession));
        Need(SourceCapabilities.ReadDocuments, session is IDocumentSourceSession, nameof(IDocumentSourceSession));
        Need(SourceCapabilities.DeltaReads, session is ISupportsDeltaReads, nameof(ISupportsDeltaReads));
        Need(SourceCapabilities.AsyncJobs, session is ISupportsAsyncJobs, nameof(ISupportsAsyncJobs));
        Need(SourceCapabilities.ChangeFeed, session is ISupportsChangeFeed, nameof(ISupportsChangeFeed));
        Need(SourceCapabilities.UnitMetadata, session is ISupportsUnitContext, nameof(ISupportsUnitContext));
        return Task.FromResult(missing.Count == 0 ? Pass(SessionInterfaces, "The session implements every declared capability.")
            : Fail(SessionInterfaces, string.Join("; ", missing) + "."));
    }

    private static List<(string, string)> Seed(int count, string prefix = "conf.key") =>
        [.. Enumerable.Range(0, count).Select(i => ($"{prefix}{i:D3}", $"value {i}"))];

    private static async Task<(List<RemoteUnitPage> Pages, Exception? Error)> ReadAllAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        var pages = new List<RemoteUnitPage>();
        try
        {
            await foreach (var page in session.ReadUnitsAsync(new UnitReadRequest([fixture.ResourceId], [fixture.Language]), cancellationToken).ConfigureAwait(false))
                pages.Add(page);
            return (pages, null);
        }
        catch (SourceException ex)
        {
            return (pages, ex);
        }
    }

    private static async Task<ConformanceResult> CheckPaginationAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        var seeded = Seed(fixture.UnitsForPagination);
        await fixture.SeedAsync(seeded, cancellationToken).ConfigureAwait(false);
        var (pages, error) = await ReadAllAsync(session, fixture, cancellationToken).ConfigureAwait(false);
        if (error is not null) return Fail(ReadPagination, $"The read failed: {error.Message}");
        if (pages.Count == 0) return Fail(ReadPagination, "The read returned no pages.");
        var completeAt = pages.FindIndex(p => p.IsComplete);
        if (completeAt < 0) return Fail(ReadPagination, "No page set IsComplete although the read ended normally.");
        if (completeAt != pages.Count - 1) return Fail(ReadPagination, $"Pages continued after the page marked complete ({pages.Count - completeAt - 1} more).");
        var read = pages.SelectMany(p => p.Units).Where(u => u.Language == fixture.Language).Select(u => u.Key.Name).ToList();
        var repeated = read.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repeated.Count > 0) return Fail(ReadPagination, $"Units were returned more than once: {string.Join(", ", repeated.Take(5))}.");
        var missing = seeded.Select(s => s.Item1).Except(read, StringComparer.Ordinal).ToList();
        if (missing.Count > 0) return Fail(ReadPagination, $"{missing.Count} seeded units were not read, e.g. {missing[0]}.");
        var wrong = pages.SelectMany(p => p.Units).FirstOrDefault(u => seeded.Any(s => s.Item1 == u.Key.Name && s.Item2 != u.Value));
        if (wrong is not null) return Fail(ReadPagination, $"{wrong.Key.Name} read as '{wrong.Value}'.");
        return Pass(ReadPagination, $"{seeded.Count} units over {pages.Count} pages, each exactly once, last page complete.");
    }

    private static async Task<ConformanceResult> CheckDroppedReadAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        await fixture.SeedAsync(Seed(fixture.UnitsForPagination), cancellationToken).ConfigureAwait(false);
        fixture.Faults!.DropReadAfter(1);
        var (pages, error) = await ReadAllAsync(session, fixture, cancellationToken).ConfigureAwait(false);
        if (error is not null) return Pass(ReadCompleteness, $"The interrupted read failed with {error.GetType().Name}, so nothing was inferred from it.");
        var total = pages.SelectMany(p => p.Units).Select(u => u.Key.Name).Distinct(StringComparer.Ordinal).Count();
        if (pages.Any(p => p.IsComplete) && total < fixture.UnitsForPagination)
            return Fail(ReadCompleteness, $"The read was cut short ({total} of {fixture.UnitsForPagination} units) but a page said IsComplete. The host would infer deletions.");
        return Pass(ReadCompleteness, "An interrupted read was not reported complete.");
    }

    private static async Task<ConformanceResult> CheckReadCancellationAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        await fixture.SeedAsync(Seed(3), cancellationToken).ConfigureAwait(false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await foreach (var _ in session.ReadUnitsAsync(new UnitReadRequest(), cancelled.Token).ConfigureAwait(false))
                return Fail(ReadCancellation, "A cancelled read still returned a page.");
            return Fail(ReadCancellation, "A cancelled read ended without throwing OperationCanceledException.");
        }
        catch (OperationCanceledException)
        {
            return Pass(ReadCancellation, "A cancelled read throws OperationCanceledException.");
        }
    }

    private static async Task<IReadOnlyList<UnitOperationResult>> WriteAndWaitAsync(IUnitSourceSession session, UnitWriteBatch batch, CancellationToken cancellationToken)
    {
        var result = await session.WriteUnitsAsync(batch, cancellationToken).ConfigureAwait(false);
        if (result.Job is null) return result.Results;
        if (session is not ISupportsAsyncJobs jobs) throw new InvalidOperationException("The write returned a job but the session does not implement ISupportsAsyncJobs.");
        for (var i = 0; i < MaxJobPolls; i++)
        {
            var status = await jobs.GetJobAsync(result.Job, cancellationToken).ConfigureAwait(false);
            if (status.State == SourceJobState.Succeeded) return status.Results ?? throw new InvalidOperationException("A finished job reported no results.");
            if (status.State == SourceJobState.Failed) throw new InvalidOperationException($"The job failed: {status.Error?.Message}");
            if (result.Job.PollAfter is { } wait && wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException($"The job did not finish after {MaxJobPolls} polls.");
    }

    private static RemoteKey Key(ISourceConformanceFixture fixture, string name) => new(fixture.ResourceId, name);

    private static async Task<RemoteUnit?> ReadUnitAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, string key, CancellationToken cancellationToken)
    {
        var (pages, error) = await ReadAllAsync(session, fixture, cancellationToken).ConfigureAwait(false);
        if (error is not null) throw error;
        return pages.SelectMany(p => p.Units).LastOrDefault(u => u.Key.Name == key && u.Language == fixture.Language);
    }

    private static async Task<ConformanceResult> CheckWriteResultsAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        var seeded = Seed(3, "conf.write");
        await fixture.SeedAsync(seeded, cancellationToken).ConfigureAwait(false);
        var ops = seeded.Select(s => (UnitWriteOperation)new PutValue(Guid.NewGuid(), Key(fixture, s.Item1), fixture.Language, s.Item2 + " (changed)")).ToList();
        var results = await WriteAndWaitAsync(session, new UnitWriteBatch(ops), cancellationToken).ConfigureAwait(false);
        if (results.Count != ops.Count) return Fail(WriteResults, $"{ops.Count} operations returned {results.Count} results.");
        for (var i = 0; i < ops.Count; i++)
        {
            if (results[i].OperationId != ops[i].OperationId)
                return Fail(WriteResults, $"Result {i} is for operation {results[i].OperationId}, expected {ops[i].OperationId}: results must keep the batch's order and IDs.");
            if (results[i].Outcome is not (UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied))
                return Fail(WriteResults, $"Operation {i} ended {results[i].Outcome}: {results[i].Error?.Message}");
        }
        foreach (var (key, value) in seeded)
        {
            var now = await fixture.ValueOfAsync(key, cancellationToken).ConfigureAwait(false);
            if (now != value + " (changed)") return Fail(WriteResults, $"{key} is '{now}' on the service after an applied write.");
        }
        return Pass(WriteResults, "One result per operation, in order, with the host's IDs, and the service holds the values.");
    }

    private static async Task<ConformanceResult> CheckWriteIdempotencyAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string key = "conf.idempotent";
        await fixture.SeedAsync([(key, "original")], cancellationToken).ConfigureAwait(false);
        var op = new PutValue(Guid.NewGuid(), Key(fixture, key), fixture.Language, "from Toucan");
        await WriteAndWaitAsync(session, new UnitWriteBatch([op]), cancellationToken).ConfigureAwait(false);
        await fixture.EditRemotelyAsync(key, "from someone else", cancellationToken).ConfigureAwait(false);
        var repeat = (await WriteAndWaitAsync(session, new UnitWriteBatch([op]), cancellationToken).ConfigureAwait(false)).Single();
        var now = await fixture.ValueOfAsync(key, cancellationToken).ConfigureAwait(false);
        if (repeat.Outcome == UnitOperationOutcome.AlreadyApplied && now == "from someone else")
            return Pass(WriteIdempotency, "A repeated operation ID is recognised and not applied twice.");
        if (repeat.Outcome == UnitOperationOutcome.AlreadyApplied)
            return Fail(WriteIdempotency, $"The repeat was reported AlreadyApplied but the value changed to '{now}'.");
        return Skip(WriteIdempotency, "The service does not recognise idempotency keys: a repeat was applied again. The host reads before retrying, so this is allowed but slower.");
    }

    private static async Task<ConformanceResult> CheckCreateIdempotencyAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string key = "conf.created";
        var op = new CreateRemoteKey(Guid.NewGuid(), Key(fixture, key), new Dictionary<string, string> { [fixture.Language] = "new" });
        var first = (await WriteAndWaitAsync(session, new UnitWriteBatch([op]), cancellationToken).ConfigureAwait(false)).Single();
        if (first.Outcome != UnitOperationOutcome.Applied) return Fail(CreateIdempotency, $"Creating a key ended {first.Outcome}: {first.Error?.Message}");
        var repeat = (await WriteAndWaitAsync(session, new UnitWriteBatch([op]), cancellationToken).ConfigureAwait(false)).Single();
        var count = await fixture.CountKeysAsync(key, cancellationToken).ConfigureAwait(false);
        if (count != 1) return Fail(CreateIdempotency, $"Repeating a create left {count} keys named {key}.");
        return repeat.Outcome is UnitOperationOutcome.AlreadyApplied or UnitOperationOutcome.Conflict or UnitOperationOutcome.Applied
            ? Pass(CreateIdempotency, $"A repeated create left one key (repeat reported {repeat.Outcome}).")
            : Fail(CreateIdempotency, $"A repeated create ended {repeat.Outcome}; expected AlreadyApplied or Conflict.");
    }

    private static async Task<ConformanceResult> CheckConflictAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string key = "conf.conflict";
        await fixture.SeedAsync([(key, "base")], cancellationToken).ConfigureAwait(false);
        var unit = await ReadUnitAsync(session, fixture, key, cancellationToken).ConfigureAwait(false);
        if (unit?.Revision is null) return Fail(ConflictDetection, "Units carry no Revision although the adapter declares ConditionalWrites.");
        await fixture.EditRemotelyAsync(key, "their edit", cancellationToken).ConfigureAwait(false);
        var result = (await WriteAndWaitAsync(session, new UnitWriteBatch([new PutValue(Guid.NewGuid(), unit.Key, fixture.Language, "our edit", unit.Revision)]),
            cancellationToken).ConfigureAwait(false)).Single();
        var now = await fixture.ValueOfAsync(key, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != UnitOperationOutcome.Conflict)
            return Fail(ConflictDetection, $"A write with a stale revision ended {result.Outcome} and the service now holds '{now}': a concurrent edit was overwritten.");
        if (now != "their edit") return Fail(ConflictDetection, $"The conflicting write was reported but the value changed to '{now}'.");
        return Pass(ConflictDetection, "A stale revision is refused as Conflict and the other edit is kept.");
    }

    private static async Task<ConformanceResult> CheckAmbiguousAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string key = "conf.ambiguous";
        await fixture.SeedAsync([(key, "before")], cancellationToken).ConfigureAwait(false);
        fixture.Faults!.TimeoutAfterCommitOnce(SourceCall.WriteUnits);
        UnitOperationResult result;
        try
        {
            result = (await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), Key(fixture, key), fixture.Language, "after")]), cancellationToken)
                .ConfigureAwait(false)).Results.Single();
        }
        catch (SourceException ex)
        {
            return Fail(AmbiguousFailure, $"WriteUnitsAsync threw {ex.Error.Kind} instead of reporting the operation Unknown.");
        }
        var now = await fixture.ValueOfAsync(key, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            UnitOperationOutcome.Unknown or UnitOperationOutcome.None => Pass(AmbiguousFailure, "A write whose answer was lost is reported Unknown."),
            UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied when now == "after" => Pass(AmbiguousFailure, "The adapter confirmed the write itself."),
            _ => Fail(AmbiguousFailure, $"A write that landed (the service holds '{now}') was reported {result.Outcome}. The host would send it again."),
        };
    }

    private static async Task<ConformanceResult> CheckExpiredTokenAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        await fixture.SeedAsync(Seed(2), cancellationToken).ConfigureAwait(false);
        fixture.Faults!.ExpireTokenOnce(SourceCall.ReadUnits);
        var (_, error) = await ReadAllAsync(session, fixture, cancellationToken).ConfigureAwait(false);
        return error switch
        {
            null => Pass(ExpiredToken, "The adapter refreshed the credential and the read succeeded."),
            SourceException { Error.Kind: SourceErrorKind.Authentication } => Pass(ExpiredToken, "An expired token is reported as Authentication."),
            SourceException ex => Fail(ExpiredToken, $"An expired token was reported as {ex.Error.Kind}; the host would not ask the user to sign in."),
            _ => Fail(ExpiredToken, $"An expired token threw {error.GetType().Name}."),
        };
    }

    private static async Task<ConformanceResult> CheckRateLimitAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        await fixture.SeedAsync(Seed(2), cancellationToken).ConfigureAwait(false);
        fixture.Faults!.RateLimitOnce(SourceCall.ReadUnits, TimeSpan.FromSeconds(7));
        var (_, error) = await ReadAllAsync(session, fixture, cancellationToken).ConfigureAwait(false);
        return error switch
        {
            null => Pass(RateLimit, "The adapter waited and the read succeeded."),
            SourceException { Error.Kind: SourceErrorKind.RateLimited, Error.RetryAfter: not null } => Pass(RateLimit, "Rate limiting is reported with the service's retry delay."),
            SourceException { Error.Kind: SourceErrorKind.RateLimited } => Pass(RateLimit, "Rate limiting is reported (without the retry delay the service sent)."),
            SourceException ex => Fail(RateLimit, $"Rate limiting was reported as {ex.Error.Kind}."),
            _ => Fail(RateLimit, $"Rate limiting threw {error.GetType().Name}."),
        };
    }

    private static async Task<ConformanceResult> CheckWriteCancellationAsync(IUnitSourceSession session, ISourceConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string key = "conf.cancelled";
        await fixture.SeedAsync([(key, "before")], cancellationToken).ConfigureAwait(false);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), Key(fixture, key), fixture.Language, "after")]), cancelled.Token).ConfigureAwait(false);
            return Fail(WriteCancellation, "A cancelled write did not throw OperationCanceledException.");
        }
        catch (OperationCanceledException)
        {
            var now = await fixture.ValueOfAsync(key, cancellationToken).ConfigureAwait(false);
            return now == "before" ? Pass(WriteCancellation, "A cancelled write throws and sends nothing.")
                : Fail(WriteCancellation, "A write cancelled before it started still changed the service.");
        }
    }

    private static async Task<ConformanceResult> CheckDocumentsAsync(IDocumentSourceSession session, IDocumentConformanceFixture fixture, CancellationToken cancellationToken)
    {
        var seeded = new Dictionary<string, byte[]> { ["conf/en.json"] = "{\"a\":\"A\"}"u8.ToArray(), ["conf/fr.json"] = "{\"a\":\"Â\"}"u8.ToArray() };
        foreach (var (path, bytes) in seeded) await fixture.SeedDocumentAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        var pages = new List<RemoteDocumentPage>();
        await foreach (var page in session.ListDocumentsAsync(new DocumentListRequest("conf/"), cancellationToken).ConfigureAwait(false)) pages.Add(page);
        if (pages.Count == 0 || !pages[^1].IsComplete || pages.Take(pages.Count - 1).Any(p => p.IsComplete))
            return Fail(Documents, "The listing did not end with exactly one page marked complete.");
        var listed = pages.SelectMany(p => p.Documents).ToList();
        if (listed.Count != seeded.Count || listed.Select(d => d.Ref.Path).Except(seeded.Keys, StringComparer.Ordinal).Any())
            return Fail(Documents, $"Listed {string.Join(", ", listed.Select(d => d.Ref.Path))}; expected {string.Join(", ", seeded.Keys)}, each once.");
        foreach (var info in listed)
        {
            var content = await session.ReadDocumentAsync(info.Ref, cancellationToken).ConfigureAwait(false);
            if (content.Version != info.Version) return Fail(Documents, $"{info.Ref.Path} read with version {content.Version}, listed as {info.Version}.");
            if (!content.Bytes.Span.SequenceEqual(seeded[info.Ref.Path])) return Fail(Documents, $"{info.Ref.Path} read with different bytes.");
        }
        return Pass(Documents, "Documents are listed once each and read byte-exact with their listed versions.");
    }

    private static async Task<ConformanceResult> CheckDocumentConflictsAsync(IDocumentSourceSession session, IDocumentConformanceFixture fixture, CancellationToken cancellationToken)
    {
        const string path = "conf/conflict.json";
        await fixture.SeedDocumentAsync(path, "1"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        var original = await session.ReadDocumentAsync(new RemoteDocumentRef(path), cancellationToken).ConfigureAwait(false);
        await fixture.SeedDocumentAsync(path, "2"u8.ToArray(), cancellationToken).ConfigureAwait(false);

        var stale = await session.WriteDocumentAsync(new RemoteDocumentWrite(Guid.NewGuid(), original.Ref, "3"u8.ToArray(), original.Version), cancellationToken).ConfigureAwait(false);
        if (stale.Outcome != UnitOperationOutcome.Conflict) return Fail(DocumentConflicts, $"A write with a stale version ended {stale.Outcome}.");
        if (!(await fixture.DocumentAsync(path, cancellationToken).ConfigureAwait(false) ?? []).AsSpan().SequenceEqual("2"u8))
            return Fail(DocumentConflicts, "A refused write still changed the document.");

        var create = await session.WriteDocumentAsync(new RemoteDocumentWrite(Guid.NewGuid(), original.Ref, "4"u8.ToArray(), null), cancellationToken).ConfigureAwait(false);
        if (create.Outcome != UnitOperationOutcome.Conflict) return Fail(DocumentConflicts, $"Creating a document that exists ended {create.Outcome}.");

        var current = await session.ReadDocumentAsync(original.Ref, cancellationToken).ConfigureAwait(false);
        var ok = await session.WriteDocumentAsync(new RemoteDocumentWrite(Guid.NewGuid(), original.Ref, "5"u8.ToArray(), current.Version), cancellationToken).ConfigureAwait(false);
        if (ok.Outcome != UnitOperationOutcome.Applied || ok.NewVersion is null || ok.NewVersion == current.Version)
            return Fail(DocumentConflicts, $"A write with the current version ended {ok.Outcome} with version {ok.NewVersion ?? "none"}.");
        return Pass(DocumentConflicts, "Stale and create-over-existing writes are refused; a current write returns a new version.");
    }
}

/// <summary>A conformance fixture over a <see cref="FakeSourceServer"/>, with fault injection and documents.</summary>
public sealed class FakeServerFixture(FakeSourceServer server, string language = "fr") : ISourceConformanceFixture, IDocumentConformanceFixture, ISourceFaultInjector
{
    public FakeSourceServer Server { get; } = server ?? throw new ArgumentNullException(nameof(server));
    public string ResourceId => Server.Resources[0].Id;
    public string Language { get; } = language;
    public int UnitsForPagination => Server.PageSize * 2 + 3;
    public ISourceFaultInjector? Faults => this;

    public ISourceSessionContext CreateContext() => new SourceTestContext(new SourceConnectionInfo
    {
        ConnectionId = "con-conformance",
        AdapterId = FakeSourceAdapter.DefaultId,
        BaseUrl = new Uri("https://source.test/"),
        RemoteProjectId = Server.ProjectId,
        Languages = Server.Languages.ToDictionary(l => l, l => l),
    });

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        foreach (var key in Server.Units.Select(u => (u.Key.ResourceId, u.Key.Name)).Distinct().ToList()) Server.RemoveKey(key.ResourceId, key.Name);
        return Task.CompletedTask;
    }

    public Task SeedAsync(IReadOnlyList<(string Key, string Value)> units, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(units);
        foreach (var (key, value) in units) Server.Put(ResourceId, key, Language, value);
        return Task.CompletedTask;
    }

    public Task EditRemotelyAsync(string key, string value, CancellationToken cancellationToken)
    {
        Server.Put(ResourceId, key, Language, value);
        return Task.CompletedTask;
    }

    public Task<string?> ValueOfAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Server.Get(ResourceId, key, Language)?.Value);

    public Task<int> CountKeysAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Server.Units.Where(u => u.Key.Name == key).Select(u => u.Key.RemoteId).Distinct().Count());

    public Task SeedDocumentAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Server.PutDocument(path, bytes);
        return Task.CompletedTask;
    }

    public Task<byte[]?> DocumentAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Server.GetDocument(path));

    public void DropReadAfter(int pages) => Server.Inject(new DroppedPageFault(pages));
    public void ExpireTokenOnce(SourceCall target) => Server.Inject(new TokenExpiredFault(target));
    public void RateLimitOnce(SourceCall target, TimeSpan retryAfter) => Server.Inject(new RateLimitFault(target, retryAfter));
    public void TimeoutAfterCommitOnce(SourceCall target) => Server.Inject(new TimeoutFault(target, AfterCommit: true));
}
