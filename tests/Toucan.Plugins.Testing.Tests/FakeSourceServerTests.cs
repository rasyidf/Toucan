using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

public sealed class FakeSourceServerTests
{
    private readonly FakeSourceServer _server = new() { PageSize = 2 };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(IUnitSourceSession Session, SourceTestContext Context)> OpenAsync(ISourceCredentials? credentials = null)
    {
        var context = new SourceTestContext(credentials: credentials);
        var session = await new FakeSourceAdapter(_server).OpenSessionAsync(context, Ct);
        return ((IUnitSourceSession)session, context);
    }

    private static async Task<List<RemoteUnitPage>> ReadAsync(IUnitSourceSession session)
    {
        var pages = new List<RemoteUnitPage>();
        await foreach (var page in session.ReadUnitsAsync(new UnitReadRequest(), Ct)) pages.Add(page);
        return pages;
    }

    [Fact]
    public async Task ReadsArePagedAndOnlyTheLastPageIsComplete()
    {
        for (var i = 0; i < 5; i++) _server.Put("main", $"k{i}", "en", $"v{i}");
        var (session, _) = await OpenAsync();

        var pages = await ReadAsync(session);

        Assert.Equal([2, 2, 1], pages.Select(p => p.Units.Count));
        Assert.Equal([false, false, true], pages.Select(p => p.IsComplete));
        Assert.NotNull(pages[^1].Cursor);
    }

    [Fact]
    public async Task ADroppedReadIsReportedIncompleteAndADuplicatePageIsRepeated()
    {
        for (var i = 0; i < 5; i++) _server.Put("main", $"k{i}", "en", $"v{i}");
        var (session, _) = await OpenAsync();

        _server.Inject(new DroppedPageFault(1));
        var dropped = await ReadAsync(session);
        Assert.Single(dropped);
        Assert.False(dropped[0].IsComplete);

        _server.Inject(new DuplicatePageFault(0));
        var duplicated = await ReadAsync(session);
        Assert.Equal(4, duplicated.Count);
        Assert.Equal(duplicated[0].Units, duplicated[1].Units);
    }

    [Fact]
    public async Task ConditionalWritesConflictAndRepeatedOperationIdsAreRecognised()
    {
        var original = _server.Put("main", "greeting", "fr", "Bonjour");
        var (session, _) = await OpenAsync();
        var put = new PutValue(Guid.NewGuid(), original.Key, "fr", "Salut", original.Revision);

        var first = (await session.WriteUnitsAsync(new UnitWriteBatch([put]), Ct)).Results.Single();
        Assert.Equal(UnitOperationOutcome.Applied, first.Outcome);
        Assert.NotEqual(original.Revision, first.NewRevision);

        var stale = (await session.WriteUnitsAsync(new UnitWriteBatch([put with { OperationId = Guid.NewGuid(), Value = "Coucou" }]), Ct)).Results.Single();
        Assert.Equal(UnitOperationOutcome.Conflict, stale.Outcome);
        Assert.Equal("Salut", stale.CurrentRemote!.Value);

        var repeat = (await session.WriteUnitsAsync(new UnitWriteBatch([put]), Ct)).Results.Single();
        Assert.Equal(UnitOperationOutcome.AlreadyApplied, repeat.Outcome);
        Assert.Equal(3, _server.ReceivedOperations.Count);
    }

    [Fact]
    public async Task ATimeoutAfterCommitAppliesTheWriteButReportsUnknown()
    {
        var unit = _server.Put("main", "k", "en", "before");
        var (session, _) = await OpenAsync();
        _server.Inject(new TimeoutFault(SourceCall.WriteUnits, AfterCommit: true));

        var result = (await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), unit.Key, "en", "after")]), Ct)).Results.Single();

        Assert.Equal(UnitOperationOutcome.Unknown, result.Outcome);
        Assert.Equal("after", _server.Get("main", "k", "en")!.Value);
    }

    [Fact]
    public async Task ATimeoutBeforeCommitChangesNothing()
    {
        var unit = _server.Put("main", "k", "en", "before");
        var (session, _) = await OpenAsync();
        _server.Inject(new TimeoutFault(SourceCall.WriteUnits, AfterCommit: false));

        var result = (await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), unit.Key, "en", "after")]), Ct)).Results.Single();

        Assert.Equal(UnitOperationOutcome.Unknown, result.Outcome);
        Assert.Equal("before", _server.Get("main", "k", "en")!.Value);
    }

    [Fact]
    public async Task AnExpiredTokenIsRefreshedOnce()
    {
        _server.Put("main", "k", "en", "v");
        _server.AcceptedToken = "fresh";
        var credentials = new StaticSourceCredentials("stale") { RefreshTo = "fresh" };
        var (session, _) = await OpenAsync(credentials);

        Assert.Single((await ReadAsync(session)).SelectMany(p => p.Units));
        Assert.Equal(1, credentials.Refreshes);
    }

    [Fact]
    public async Task FaultsSurfaceAsSourceErrorKinds()
    {
        var (session, _) = await OpenAsync();
        _server.Inject(new RateLimitFault(SourceCall.ReadUnits, TimeSpan.FromSeconds(3)));
        var rate = await Assert.ThrowsAsync<SourceException>(() => ReadAsync(session));
        Assert.Equal(SourceErrorKind.RateLimited, rate.Error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(3), rate.Error.RetryAfter);

        _server.Inject(new UnavailableFault(SourceCall.Describe));
        var down = await Assert.ThrowsAsync<SourceException>(() => session.DescribeAsync(Ct));
        Assert.True(down.Error.IsTransient);
        Assert.Equal(0, _server.PendingFaults);
    }

    [Fact]
    public async Task AConcurrentEditLandsBeforeTheCallIsHandled()
    {
        var unit = _server.Put("main", "k", "en", "base");
        var (session, _) = await OpenAsync();
        _server.Inject(new ConcurrentEditFault(SourceCall.WriteUnits, s => s.Put("main", "k", "en", "theirs")));

        var result = (await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), unit.Key, "en", "ours", unit.Revision)]), Ct)).Results.Single();

        Assert.Equal(UnitOperationOutcome.Conflict, result.Outcome);
        Assert.Equal("theirs", _server.Get("main", "k", "en")!.Value);
    }

    [Fact]
    public async Task RenameKeepsTheRemoteIdAndDeltaReadsReportChangesAndDeletions()
    {
        _server.Put("main", "old", "en", "v");
        _server.Put("main", "gone", "en", "x");
        var (session, _) = await OpenAsync();
        var cursor = (await ReadAsync(session))[^1].Cursor!;
        var id = _server.Get("main", "old", "en")!.Key.RemoteId;

        var results = (await session.WriteUnitsAsync(new UnitWriteBatch([
            new RenameRemoteKey(Guid.NewGuid(), new RemoteKey("main", "old"), "new"),
            new DeleteRemoteKey(Guid.NewGuid(), new RemoteKey("main", "gone")),
        ]), Ct)).Results;
        Assert.All(results, r => Assert.Equal(UnitOperationOutcome.Applied, r.Outcome));
        Assert.Equal(id, _server.Get("main", "new", "en")!.Key.RemoteId);

        var changes = new List<RemoteChangePage>();
        await foreach (var page in ((ISupportsDeltaReads)session).ReadChangesAsync(new DeltaReadRequest(cursor), Ct)) changes.Add(page);
        var delta = Assert.Single(changes);
        Assert.Equal("new", Assert.Single(delta.Changed).Key.Name);
        Assert.Contains(delta.Deleted, d => d.Key.Name == "gone" && d.Language is null);
        Assert.Contains(delta.Deleted, d => d.Key.Name == "old");
        Assert.True(delta.AuthoritativeDeletes);
    }

    [Fact]
    public async Task AsyncJobsFinishAfterPolling()
    {
        _server.UseAsyncJobs = true;
        _server.JobPolls = 2;
        var unit = _server.Put("main", "k", "en", "v");
        var (session, _) = await OpenAsync();

        var write = await session.WriteUnitsAsync(new UnitWriteBatch([new PutValue(Guid.NewGuid(), unit.Key, "en", "w")]), Ct);
        Assert.Equal(UnitOperationOutcome.Pending, write.Results.Single().Outcome);
        var jobs = (ISupportsAsyncJobs)session;
        Assert.Equal(SourceJobState.Running, (await jobs.GetJobAsync(write.Job!, Ct)).State);
        Assert.Equal(SourceJobState.Running, (await jobs.GetJobAsync(write.Job!, Ct)).State);
        var done = await jobs.GetJobAsync(write.Job!, Ct);
        Assert.Equal(SourceJobState.Succeeded, done.State);
        Assert.Equal(UnitOperationOutcome.Applied, done.Results!.Single().Outcome);
    }

    [Fact]
    public async Task DocumentsHaveVersionsAndConditionalWrites()
    {
        var version = _server.PutDocument("en.json", "{}"u8.ToArray());
        var session = (IDocumentSourceSession)(await OpenAsync()).Session;

        var read = await session.ReadDocumentAsync(new RemoteDocumentRef("en.json"), Ct);
        Assert.Equal(version, read.Version);
        var stale = await session.WriteDocumentAsync(new RemoteDocumentWrite(Guid.NewGuid(), read.Ref, "{\"a\":1}"u8.ToArray(), "nope"), Ct);
        Assert.Equal(UnitOperationOutcome.Conflict, stale.Outcome);
        var ok = await session.WriteDocumentAsync(new RemoteDocumentWrite(Guid.NewGuid(), read.Ref, "{\"a\":1}"u8.ToArray(), version), Ct);
        Assert.Equal(UnitOperationOutcome.Applied, ok.Outcome);
        Assert.Equal(UnitOperationOutcome.Applied, (await session.DeleteDocumentAsync(read.Ref, ok.NewVersion!, Guid.NewGuid(), Ct)).Outcome);
        Assert.Empty(_server.DocumentPaths);
    }

    [Fact]
    public async Task TheChangeFeedHintsAfterAChange()
    {
        var (session, _) = await OpenAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using var hints = ((ISupportsChangeFeed)session).WatchAsync(null, stop.Token).GetAsyncEnumerator(stop.Token);
        var next = hints.MoveNextAsync().AsTask();
        await Task.Yield();
        // The subscription starts on the first MoveNext; keep changing until the watcher sees one.
        for (var i = 0; !next.IsCompleted && i < 50; i++)
        {
            _server.Put("main", "k", "en", $"v{i}");
            await Task.WhenAny(next, Task.Delay(20, Ct));
        }
        Assert.True(await next);
        Assert.Equal(["main"], hints.Current.ResourceIds);
        await stop.CancelAsync();
    }

    [Fact]
    public async Task ProjectDiscoveryListsTheServerProject()
    {
        var projects = new List<RemoteProjectSummary>();
        await foreach (var p in new FakeSourceAdapter(_server).ListProjectsAsync(new SourceTestContext(), Ct)) projects.Add(p);
        Assert.Equal("fake-project", Assert.Single(projects).Id);
    }

    [Fact]
    public async Task SessionsAreCountedUntilDisposed()
    {
        var adapter = new FakeSourceAdapter(_server);
        var session = await adapter.OpenSessionAsync(new SourceTestContext(), Ct);
        Assert.Equal(1, adapter.OpenSessions);
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Equal(0, adapter.OpenSessions);
    }

    [Fact]
    public void TheTestHostChecksAdapterDescriptorsAgainstAManifest()
    {
        var host = new PluginTestHost("test.plugin")
        {
            Manifest = new PluginManifest { Id = "test.plugin", Name = "T", Version = "1.0.0", ApiVersion = "1.3", EntryAssembly = "t.dll",
                Capabilities = ["sources"], Network = new NetworkAccess { Hosts = ["other.test"] } },
        };
        var error = Assert.Throws<InvalidOperationException>(() => host.Context.AddSourceAdapter(new FakeSourceAdapter(_server)));
        Assert.Contains("'source.test'", error.Message, StringComparison.Ordinal);

        host.Manifest = host.Manifest with { Network = new NetworkAccess { Hosts = ["source.test"] } };
        host.Context.AddSourceAdapter(new FakeSourceAdapter(_server));
        Assert.Contains(FakeSourceAdapter.DefaultId, host.SourceAdapters.Keys);
    }
}
