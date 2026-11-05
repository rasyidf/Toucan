using Microsoft.AspNetCore.Builder;
using System.Net.Http.Headers;
using Toucan.Modules.Sources;
using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Toucan.Sample.SourceServer;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

/// <summary>The built-in client for the Toucan source protocol against the reference server, including the shared conformance suite.</summary>
public sealed class RestSourceTests : IAsyncLifetime
{
    private static readonly PluginManifest s_manifest = new()
    {
        Id = "toucan.sources", Name = "Toucan sources", Version = "1.0.0", ApiVersion = "1.3", EntryAssembly = "t.dll",
        Capabilities = ["sources"], Network = new NetworkAccess { UserDefinedHosts = true },
    };

    private readonly SourceStore _store = new() { PageSize = 2 };
    private WebApplication _app = null!;
    private Uri _url = null!;
    private readonly RestSourceAdapter _adapter = new();

    public async ValueTask InitializeAsync()
    {
        _app = ReferenceServer.Build(["--urls", "http://127.0.0.1:0"], _store);
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _url = new Uri(_app.Urls.First());
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private sealed class TokenHttp(string token) : ISourceHttpClientFactory
    {
        public HttpClient Create()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }

    private SourceTestContext Context(string token = ReferenceServer.DefaultToken) => new(new SourceConnectionInfo
    {
        ConnectionId = "c1", AdapterId = RestSourceAdapter.AdapterId, BaseUrl = _url, RemoteProjectId = "demo",
        Languages = new Dictionary<string, string> { ["en"] = "en", ["fr"] = "fr" },
    }, http: new TokenHttp(token));

    private sealed class Fixture(RestSourceTests owner) : ISourceConformanceFixture
    {
        public ISourceSessionContext CreateContext() => owner.Context();
        public string ResourceId => "app";
        public string Language => "fr";
        public int UnitsForPagination => 7;
        public Task ResetAsync(CancellationToken cancellationToken) { owner._store.Reset(); return Task.CompletedTask; }
        public Task SeedAsync(IReadOnlyList<(string Key, string Value)> units, CancellationToken cancellationToken)
        {
            foreach (var (key, value) in units) owner._store.Put("app", key, "fr", value);
            return Task.CompletedTask;
        }
        public Task EditRemotelyAsync(string key, string value, CancellationToken cancellationToken) { owner._store.Put("app", key, "fr", value); return Task.CompletedTask; }
        public Task<string?> ValueOfAsync(string key, CancellationToken cancellationToken) => Task.FromResult(owner._store.Get("app", key, "fr")?.Value);
        public Task<int> CountKeysAsync(string key, CancellationToken cancellationToken) => Task.FromResult(owner._store.CountKeys(key));
        public ISourceFaultInjector? Faults => null;
    }

    private async Task<IUnitSourceSession> OpenAsync(string token = ReferenceServer.DefaultToken) =>
        (IUnitSourceSession)await _adapter.OpenSessionAsync(Context(token), TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheConformanceSuitePasses()
    {
        var report = await SourceAdapterConformance.RunAsync(_adapter, s_manifest, new Fixture(this), TestContext.Current.CancellationToken);
        report.ThrowIfFailed();
    }

    [Fact]
    public async Task DescribeReportsLanguagesResourcesPermissionsAndLimits()
    {
        await using var session = await OpenAsync();
        var info = await session.DescribeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["en", "fr", "de"], info.Languages.Select(l => l.Code));
        Assert.Equal("app", Assert.Single(info.Resources).Id);
        Assert.True(info.Permissions.HasFlag(SourceCapabilities.WriteValues | SourceCapabilities.DeltaReads | SourceCapabilities.ConditionalWrites));
        Assert.Equal(100, info.Limits!.MaxBatchSize);
    }

    [Fact]
    public async Task ProjectDiscoveryListsTheProjects()
    {
        var projects = new List<RemoteProjectSummary>();
        await foreach (var p in _adapter.ListProjectsAsync(Context(), TestContext.Current.CancellationToken)) projects.Add(p);
        Assert.Equal("demo", Assert.Single(projects).Id);
    }

    [Fact]
    public async Task PagesAreFollowedAndOnlyTheLastIsComplete()
    {
        for (var i = 0; i < 5; i++) _store.Put("app", "k" + i, "fr", "v");
        await using var session = await OpenAsync();
        var pages = new List<RemoteUnitPage>();
        await foreach (var page in session.ReadUnitsAsync(new(), TestContext.Current.CancellationToken)) pages.Add(page);
        Assert.True(pages.Count >= 3);
        Assert.All(pages.SkipLast(1), p => Assert.False(p.IsComplete));
        Assert.True(pages[^1].IsComplete);
        Assert.NotNull(pages[^1].Cursor);
    }

    [Fact]
    public async Task LanguageFiltersReachTheServer()
    {
        _store.Put("app", "a", "fr", "x");
        _store.Put("app", "a", "de", "y");
        await using var session = await OpenAsync();
        var units = new List<RemoteUnit>();
        await foreach (var page in session.ReadUnitsAsync(new(null, ["de"]), TestContext.Current.CancellationToken)) units.AddRange(page.Units);
        Assert.Equal("de", Assert.Single(units).Language);
    }

    [Fact]
    public async Task DeltaReadsReturnChangesAndDeletions()
    {
        _store.Put("app", "a", "fr", "x");
        await using var session = await OpenAsync();
        string? cursor = null;
        await foreach (var page in session.ReadUnitsAsync(new(), TestContext.Current.CancellationToken)) cursor = page.Cursor ?? cursor;
        Assert.NotNull(cursor);

        _store.Put("app", "a", "fr", "changed");
        _store.Put("app", "b", "fr", "new");
        var delta = new List<RemoteChangePage>();
        await foreach (var page in ((ISupportsDeltaReads)session).ReadChangesAsync(new(cursor!), TestContext.Current.CancellationToken)) delta.Add(page);
        Assert.Equal(["a", "b"], delta.SelectMany(p => p.Changed).Select(u => u.Key.Name).Order());
        Assert.True(delta[^1].IsComplete);
        Assert.True(delta[^1].AuthoritativeDeletes);
    }

    [Fact]
    public async Task OperationsAreIdempotentConditionalAndKeepTheirOrder()
    {
        var seeded = _store.Put("app", "a", "fr", "old");
        await using var session = await OpenAsync();
        var key = new RemoteKey("app", "a", seeded.KeyId);
        var id = Guid.NewGuid();
        var batch = new UnitWriteBatch([new PutValue(id, key, "fr", "one", seeded.Revision), new PutValue(Guid.NewGuid(), key, "fr", "stale", seeded.Revision)]);

        var first = await session.WriteUnitsAsync(batch, TestContext.Current.CancellationToken);
        Assert.Equal([UnitOperationOutcome.Applied, UnitOperationOutcome.Conflict], first.Results.Select(r => r.Outcome));
        Assert.Equal("one", first.Results[1].CurrentRemote!.Value);

        var repeat = await session.WriteUnitsAsync(new([new PutValue(id, key, "fr", "one", seeded.Revision)]), TestContext.Current.CancellationToken);
        Assert.Equal(UnitOperationOutcome.AlreadyApplied, repeat.Results[0].Outcome);
        Assert.Equal(first.Results[0].NewRevision, repeat.Results[0].NewRevision);
    }

    [Fact]
    public async Task KeysAreCreatedRenamedAndDeleted()
    {
        await using var session = await OpenAsync();
        var key = new RemoteKey("app", "fresh");
        var created = (await session.WriteUnitsAsync(new([new CreateRemoteKey(Guid.NewGuid(), key, new Dictionary<string, string> { ["fr"] = "Frais", ["en"] = "Fresh" })]), TestContext.Current.CancellationToken)).Results[0];
        Assert.Equal(UnitOperationOutcome.Applied, created.Outcome);
        Assert.NotNull(created.AssignedRemoteId);

        var withId = key with { RemoteId = created.AssignedRemoteId };
        var renamed = (await session.WriteUnitsAsync(new([new RenameRemoteKey(Guid.NewGuid(), withId, "fresher")]), TestContext.Current.CancellationToken)).Results[0];
        Assert.Equal(UnitOperationOutcome.Applied, renamed.Outcome);
        Assert.NotNull(_store.Get("app", "fresher", "en"));

        var gone = (await session.WriteUnitsAsync(new([new DeleteRemoteKey(Guid.NewGuid(), withId)]), TestContext.Current.CancellationToken)).Results[0];
        Assert.Equal(UnitOperationOutcome.Applied, gone.Outcome);
        Assert.Equal(0, _store.CountKeys("fresher"));
    }

    [Fact]
    public async Task StatusesAreWrittenByMeaningAndRefusedWhenUnknown()
    {
        var seeded = _store.Put("app", "a", "fr", "x");
        await using var session = await OpenAsync();
        var key = new RemoteKey("app", "a", seeded.KeyId);
        var ok = (await session.WriteUnitsAsync(new([new PutStatus(Guid.NewGuid(), key, "fr", SourceReviewMeaning.NeedsReview)]), TestContext.Current.CancellationToken)).Results[0];
        Assert.Equal(UnitOperationOutcome.Applied, ok.Outcome);
        Assert.Equal("needsReview", _store.Get("app", "a", "fr")!.Status);

        var read = new List<RemoteUnit>();
        await foreach (var page in session.ReadUnitsAsync(new(), TestContext.Current.CancellationToken)) read.AddRange(page.Units);
        Assert.Equal(SourceReviewMeaning.NeedsReview, read.Single(u => u.Key.Name == "a").Status!.Meaning);
    }

    [Fact]
    public async Task ABadTokenIsAnAuthenticationError()
    {
        await using var session = await OpenAsync("wrong");
        var test = await session.TestAsync(TestContext.Current.CancellationToken);
        Assert.False(test.Succeeded);
        Assert.Equal(SourceErrorKind.Authentication, test.Error!.Kind);
        var failure = await Assert.ThrowsAsync<SourceException>(async () =>
        {
            await foreach (var _ in session.ReadUnitsAsync(new(), TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal(SourceErrorKind.Authentication, failure.Error.Kind);
    }

    [Fact]
    public async Task AnUnknownProjectIsNotFound()
    {
        var context = new SourceTestContext(new SourceConnectionInfo { ConnectionId = "c", AdapterId = RestSourceAdapter.AdapterId, BaseUrl = _url, RemoteProjectId = "nope" }, http: new TokenHttp(ReferenceServer.DefaultToken));
        await using var session = (IUnitSourceSession)await _adapter.OpenSessionAsync(context, TestContext.Current.CancellationToken);
        var test = await session.TestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SourceErrorKind.NotFound, test.Error!.Kind);
    }

    [Fact]
    public async Task AnUnreachableServerDuringAWriteIsReportedAsUnknownNotFailed()
    {
        await using var session = await OpenAsync();
        await _app.StopAsync(TestContext.Current.CancellationToken);
        var result = await session.WriteUnitsAsync(new([new PutValue(Guid.NewGuid(), new RemoteKey("app", "a"), "fr", "x")]), TestContext.Current.CancellationToken);
        Assert.Equal(UnitOperationOutcome.Unknown, Assert.Single(result.Results).Outcome);
    }
}
