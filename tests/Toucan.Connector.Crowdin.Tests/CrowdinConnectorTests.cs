using System.Net;
using System.Text;
using System.Text.Json;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Testing;
using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Connector.Crowdin.Tests;

/// <summary>
/// The Crowdin connector through the host's real HTTP pipeline (allow-list, credential scope, retries) against a stateful stand-in
/// for Crowdin's API, plus the shared conformance suite. The stand-in follows the API documentation; the opt-in live tests check it
/// against the real service.
/// </summary>
public sealed class CrowdinConnectorTests : IDisposable
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private readonly FakeCrowdin _crowdin = new();
    private readonly CrowdinAdapter _adapter = new();
    private readonly PluginManifest _manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plugin.json")), s_web)!;
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _crowdin.Dispose();
    }

    private SourceTestContext Context(string token = FakeCrowdin.Token, params string[] languages)
    {
        var baseUrl = new Uri("https://api.crowdin.com/api/v2/");
        var factory = new SourceHttpClientFactory(new SourceNetworkPolicy(_manifest.Network, baseUrl), new StaticSourceCredentials(token),
            new ApiTokenAuth("token", "Token"), new TestDiagnostics(),
            new SourceHttpOptions { PrimaryHandler = () => _crowdin, MaxRetries = 0, BaseRetryDelay = TimeSpan.FromMilliseconds(1) });
        _disposables.Add(factory);
        return new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = "c1", AdapterId = CrowdinAdapter.AdapterId, BaseUrl = baseUrl, RemoteProjectId = FakeCrowdin.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Languages = (languages.Length == 0 ? ["fr", "de"] : languages).ToDictionary(l => l, l => l),
        }, http: factory);
    }

    private async Task<IDocumentSourceSession> OpenAsync(string token = FakeCrowdin.Token, params string[] languages) =>
        (IDocumentSourceSession)await _adapter.OpenSessionAsync(Context(token, languages), TestContext.Current.CancellationToken);

    private async Task<List<RemoteDocumentInfo>> ListAsync(IDocumentSourceSession session, string? prefix = null)
    {
        var all = new List<RemoteDocumentInfo>();
        await foreach (var page in session.ListDocumentsAsync(new(prefix), TestContext.Current.CancellationToken)) all.AddRange(page.Documents);
        return all;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Text(ReadOnlyMemory<byte> bytes) => Encoding.UTF8.GetString(bytes.Span);

    // ───────────── registration ─────────────

    [Fact]
    public void TheManifestAndDescriptorPassTheRegistrationRules() =>
        Assert.Empty(SourceAdapterRules.Check(_adapter, _manifest));

    [Fact]
    public async Task TheSharedConformanceSuitePasses()
    {
        var fixture = new CrowdinFixture(this);
        var report = await SourceAdapterConformance.RunAsync(_adapter, _manifest, fixture, TestContext.Current.CancellationToken);
        Assert.Empty(report.Failures);
    }

    private sealed class CrowdinFixture(CrowdinConnectorTests owner) : ISourceConformanceFixture, IDocumentConformanceFixture
    {
        public ISourceSessionContext CreateContext() => owner.Context();
        public string ResourceId => "unused";
        public string Language => "fr";
        public int UnitsForPagination => 1;
        public ISourceFaultInjector? Faults => null;
        public Task ResetAsync(CancellationToken cancellationToken) { owner._crowdin.Files.Clear(); return Task.CompletedTask; }
        public Task SeedAsync(IReadOnlyList<(string Key, string Value)> units, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EditRemotelyAsync(string key, string value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> ValueOfAsync(string key, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<int> CountKeysAsync(string key, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task SeedDocumentAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            // Another translator changes the source file in Crowdin.
            var existing = owner._crowdin.Files.FirstOrDefault(f => f.Path == path);
            if (existing is not null) existing.Source = bytes;
            else owner._crowdin.AddFile(path, Encoding.UTF8.GetString(bytes));
            return Task.CompletedTask;
        }

        public Task<byte[]?> DocumentAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(owner._crowdin.Files.FirstOrDefault(f => f.Path == path)?.Source);
    }

    // ───────────── describe and list ─────────────

    [Fact]
    public async Task DescribeReportsLanguagesFilesAndLimits()
    {
        _crowdin.AddFile("locales/en/common.json", "{}");
        await using var session = await OpenAsync();

        var info = await session.DescribeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Demo", info.Name);
        Assert.Equal(["en", "fr", "de"], info.Languages.Select(l => l.Code));
        Assert.True(info.Languages[0].IsSource);
        Assert.Equal("locales/en/common.json", Assert.Single(info.Resources).Path?.TrimStart('/'));
        Assert.Equal(2, info.Limits!.MaxConcurrency);
    }

    [Fact]
    public async Task EachFileListsOneDocumentPerLanguageUsingTheExportPattern()
    {
        _crowdin.AddFile("locales/en/common.json", """{"a":"A"}""");
        await using var session = await OpenAsync();

        var docs = await ListAsync(session);

        Assert.Equal(["locales/de-DE/common.json", "locales/en/common.json", "locales/fr-FR/common.json"], docs.Select(d => d.Ref.Path).Order(StringComparer.Ordinal));
        Assert.All(docs, d => Assert.StartsWith("sha256:", d.Version, StringComparison.Ordinal));
        Assert.Equal("fr", docs.Single(d => d.Ref.Path == "locales/fr-FR/common.json").Ref.Language);
    }

    [Fact]
    public async Task LanguagesTheConnectionDoesNotMapAreNotListed()
    {
        _crowdin.AddFile("common.json", "{}");
        await using var session = await OpenAsync(languages: "fr");
        var paths = (await ListAsync(session)).Select(d => d.Ref.Path).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["common.json", "locales/fr-FR/common.json"], paths);
    }

    [Fact]
    public async Task WithoutAnExportPatternLanguagesGoUnderTheirOwnFolder()
    {
        _crowdin.AddFile("strings/app.json", "{}", exportPattern: null);
        await using var session = await OpenAsync();
        Assert.Equal(["de/strings/app.json", "fr/strings/app.json", "strings/app.json"], (await ListAsync(session)).Select(d => d.Ref.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task APatternThatDoesNotNameTheLanguageIsRefusedBecauseDocumentsWouldCollide()
    {
        _crowdin.AddFile("a.json", "{}", exportPattern: "/out/%original_file_name%");
        await using var session = await OpenAsync();
        var failure = await Assert.ThrowsAsync<SourceException>(() => ListAsync(session));
        Assert.Equal(SourceErrorKind.Validation, failure.Error.Kind);
        Assert.Contains("%locale%", failure.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManyFilesArePagedAndListedOnce()
    {
        for (var i = 0; i < 230; i++) _crowdin.AddFile($"f/{i:D3}.json", "{}");
        await using var session = await OpenAsync(languages: "fr");
        var pages = new List<RemoteDocumentPage>();
        await foreach (var page in session.ListDocumentsAsync(new(), TestContext.Current.CancellationToken)) pages.Add(page);
        Assert.True(pages.Count >= 3);
        Assert.All(pages.SkipLast(1), p => Assert.False(p.IsComplete));
        Assert.True(pages[^1].IsComplete);
        Assert.Equal(460, pages.SelectMany(p => p.Documents).Select(d => d.Ref.Path).Distinct().Count());
    }

    [Fact]
    public async Task DiscoveryListsTheProjects()
    {
        var projects = new List<RemoteProjectSummary>();
        await foreach (var p in _adapter.ListProjectsAsync(Context(), TestContext.Current.CancellationToken)) projects.Add(p);
        Assert.Equal(("7", "Demo"), (Assert.Single(projects).Id, projects[0].Name));
    }

    // ───────────── reading ─────────────

    [Fact]
    public async Task ReadingReturnsTheExportedBytesAndTheTokenNeverReachesTheStorageHost()
    {
        var file = _crowdin.AddFile("locales/en/common.json", """{"a":"A"}""");
        file.Translations["fr"] = """{"a":"Â"}"""u8.ToArray();
        await using var session = await OpenAsync();

        var content = await session.ReadDocumentAsync(new("locales/fr-FR/common.json"), TestContext.Current.CancellationToken);

        Assert.Equal("""{"a":"Â"}""", Text(content.Bytes));
        Assert.Equal(Hash(content.Bytes.Span), content.Version);
        Assert.Contains(_crowdin.Requests, r => r.Url.Host == FakeCrowdin.FileHost && !r.HasAuthorization);
        Assert.All(_crowdin.Requests.Where(r => r.Url.Host == FakeCrowdin.ApiHost), r => Assert.True(r.HasAuthorization));
    }

    [Fact]
    public async Task ReadingAFileAddedInCrowdinAfterTheLastListingFindsIt()
    {
        await using var session = await OpenAsync();
        await ListAsync(session);
        _crowdin.AddFile("late.json", "{}");
        var content = await session.ReadDocumentAsync(new("late.json"), TestContext.Current.CancellationToken);
        Assert.Equal("{}", Text(content.Bytes));
    }

    [Fact]
    public async Task AMissingDocumentIsNotFound()
    {
        await using var session = await OpenAsync();
        var failure = await Assert.ThrowsAsync<SourceException>(() => session.ReadDocumentAsync(new("nope.json"), TestContext.Current.CancellationToken));
        Assert.Equal(SourceErrorKind.NotFound, failure.Error.Kind);
    }

    // ───────────── writing ─────────────

    [Fact]
    public async Task WritingATranslationImportsItAndReturnsTheVersionCrowdinNowExports()
    {
        var file = _crowdin.AddFile("locales/en/common.json", """{"a":"A"}""");
        file.Translations["fr"] = """{"a":"old"}"""u8.ToArray();
        await using var session = await OpenAsync();
        var read = await session.ReadDocumentAsync(new("locales/fr-FR/common.json"), TestContext.Current.CancellationToken);

        var result = await session.WriteDocumentAsync(new(Guid.NewGuid(), read.Ref, """{"a":"new"}"""u8.ToArray(), read.Version), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Applied, result.Outcome);
        Assert.Equal("""{"a":"new"}""", Encoding.UTF8.GetString(file.Translations["fr"]));
        Assert.Equal("""{"a":"A"}""", Encoding.UTF8.GetString(file.Source));
        Assert.Equal(Hash(file.Translations["fr"]), result.NewVersion);
    }

    [Fact]
    public async Task WritingTheSourceLanguageUpdatesTheSourceFile()
    {
        var file = _crowdin.AddFile("locales/en/common.json", """{"a":"A"}""");
        await using var session = await OpenAsync();
        var read = await session.ReadDocumentAsync(new("locales/en/common.json"), TestContext.Current.CancellationToken);

        var result = await session.WriteDocumentAsync(new(Guid.NewGuid(), read.Ref, """{"a":"A2"}"""u8.ToArray(), read.Version), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Applied, result.Outcome);
        Assert.Equal("""{"a":"A2"}""", Encoding.UTF8.GetString(file.Source));
    }

    [Fact]
    public async Task AStaleVersionIsAConflictAndNothingIsUploaded()
    {
        var file = _crowdin.AddFile("locales/en/common.json", "{}");
        file.Translations["fr"] = "one"u8.ToArray();
        await using var session = await OpenAsync();
        var read = await session.ReadDocumentAsync(new("locales/fr-FR/common.json"), TestContext.Current.CancellationToken);
        file.Translations["fr"] = "two"u8.ToArray();

        var result = await session.WriteDocumentAsync(new(Guid.NewGuid(), read.Ref, "three"u8.ToArray(), read.Version), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Conflict, result.Outcome);
        Assert.Equal("two", Encoding.UTF8.GetString(file.Translations["fr"]));
        Assert.DoesNotContain(_crowdin.Requests, r => r.Method == HttpMethod.Post && r.Url.AbsolutePath.EndsWith("/storages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreatingOverAnExistingDocumentConflictsAndUnknownPathsAreRefusedWithAReason()
    {
        _crowdin.AddFile("locales/en/common.json", "{}");
        await using var session = await OpenAsync();

        var exists = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("locales/fr-FR/common.json"), "x"u8.ToArray(), null), TestContext.Current.CancellationToken);
        var unknown = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("brand-new.json"), "x"u8.ToArray(), null), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Conflict, exists.Outcome);
        Assert.Equal(UnitOperationOutcome.Refused, unknown.Outcome);
        Assert.Equal(SourceErrorKind.Unsupported, unknown.Error!.Kind);
        Assert.Contains("Create the file in Crowdin", unknown.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletingIsLeftToCrowdin()
    {
        _crowdin.AddFile("a.json", "{}");
        await using var session = await OpenAsync();
        var result = await session.DeleteDocumentAsync(new("a.json"), "v", Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(UnitOperationOutcome.Refused, result.Outcome);
        Assert.Single(_crowdin.Files);
    }

    [Fact]
    public async Task AConnectionDroppedAfterTheImportIsReportedAsUnknownNotFailed()
    {
        var file = _crowdin.AddFile("locales/en/common.json", "{}");
        file.Translations["fr"] = "old"u8.ToArray();
        await using var session = await OpenAsync();
        var read = await session.ReadDocumentAsync(new("locales/fr-FR/common.json"), TestContext.Current.CancellationToken);
        _crowdin.DropAfterNextImport = true;

        var result = await session.WriteDocumentAsync(new(Guid.NewGuid(), read.Ref, "new"u8.ToArray(), read.Version), TestContext.Current.CancellationToken);

        Assert.Equal(UnitOperationOutcome.Unknown, result.Outcome);
        // It did land: the host reads before it decides, and finds the new text.
        Assert.Equal("new", Encoding.UTF8.GetString(file.Translations["fr"]));
    }

    // ───────────── errors ─────────────

    [Fact]
    public async Task ABadTokenIsAnAuthenticationError()
    {
        await using var session = await OpenAsync("wrong");
        var test = await session.TestAsync(TestContext.Current.CancellationToken);
        Assert.False(test.Succeeded);
        Assert.Equal(SourceErrorKind.Authentication, test.Error!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, SourceErrorKind.Authorization)]
    [InlineData(HttpStatusCode.NotFound, SourceErrorKind.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, SourceErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, SourceErrorKind.Unavailable)]
    public async Task HttpFailuresMapToSourceErrors(HttpStatusCode status, SourceErrorKind expected)
    {
        await using var session = await OpenAsync();
        _crowdin.FailNext = ("projects/7", status, status == HttpStatusCode.TooManyRequests ? 3 : null);
        var test = await session.TestAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, test.Error!.Kind);
        if (expected == SourceErrorKind.RateLimited) Assert.Equal(TimeSpan.FromSeconds(3), test.Error.RetryAfter);
    }

    // ───────────── live (opt in) ─────────────

    [Fact]
    public async Task LiveReadOnlyCheckAgainstARealProject()
    {
        var token = Environment.GetEnvironmentVariable("TOUCAN_CROWDIN_TOKEN");
        var project = Environment.GetEnvironmentVariable("TOUCAN_CROWDIN_PROJECT");
        Assert.SkipWhen(string.IsNullOrEmpty(token) || string.IsNullOrEmpty(project), "Set TOUCAN_CROWDIN_TOKEN and TOUCAN_CROWDIN_PROJECT to run against Crowdin. Read-only: nothing is written.");

        var baseUrl = new Uri(Environment.GetEnvironmentVariable("TOUCAN_CROWDIN_URL") ?? "https://api.crowdin.com/api/v2/");
        using var factory = new SourceHttpClientFactory(new SourceNetworkPolicy(_manifest.Network, baseUrl), new StaticSourceCredentials(token!), new ApiTokenAuth("token", "Token"), new TestDiagnostics());
        var context = new SourceTestContext(new SourceConnectionInfo { ConnectionId = "live", AdapterId = CrowdinAdapter.AdapterId, BaseUrl = baseUrl, RemoteProjectId = project! }, http: factory);
        await using var session = (IDocumentSourceSession)await _adapter.OpenSessionAsync(context, TestContext.Current.CancellationToken);

        Assert.True((await session.TestAsync(TestContext.Current.CancellationToken)).Succeeded);
        var info = await session.DescribeAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(info.Languages);
        await foreach (var page in session.ListDocumentsAsync(new(), TestContext.Current.CancellationToken))
        {
            foreach (var doc in page.Documents.Take(2))
            {
                var content = await session.ReadDocumentAsync(doc.Ref, TestContext.Current.CancellationToken);
                Assert.Equal(doc.Version, content.Version);
            }
            break;
        }
    }
}
