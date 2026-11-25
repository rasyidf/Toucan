using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Toucan.Core;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Connector.Crowdin.Tests;

/// <summary>
/// R11.4 for Crowdin: connect, first pull, offline edit, restart, reconnect, a remote edit meanwhile, conflict, push, reopen, and
/// no secret in the diagnostics. Runs the real sync coordinator and the host's HTTP pipeline against the Crowdin stand-in.
/// </summary>
public sealed class CrowdinWorkflowTests : IDisposable
{
    private static readonly JsonSerializerOptions s_web = new(JsonSerializerDefaults.Web);
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-crowdin-").FullName;
    private readonly FakeCrowdin _crowdin = new();
    private readonly CrowdinAdapter _adapter = new();
    private readonly TestDiagnostics _diagnostics = new();
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly PluginManifest _manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plugin.json")), s_web)!;
    private readonly List<SourceConnectionSettings> _settings =
    [
        new() { Id = "crowdin", Name = "Crowdin", Adapter = CrowdinAdapter.AdapterId, Languages = { ["fr"] = "fr" }, RemoteProject = new() { Id = "7" } },
    ];
    private readonly CrowdinFile _file;
    private readonly List<IDisposable> _disposables = [];

    private sealed record CrowdinFile(FakeCrowdin.CrowdinFile Remote);

    public CrowdinWorkflowTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        _services = services.BuildServiceProvider();
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _diagnostics.RegisterSecret(FakeCrowdin.Token);

        var remote = _crowdin.AddFile("locales/en/common.json", """{"title":"Welcome"}""");
        remote.Translations["fr"] = """{"title":"Bienvenue"}"""u8.ToArray();
        _file = new CrowdinFile(remote);
        Write("locales/en-US/common.json", """{"title":"Welcome"}""");
        Write("locales/fr-FR/common.json", """{"title":"Bienvenue"}""");
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _crowdin.Dispose();
        _workspace.Dispose();
        _services.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    /// <summary>Opening the project again: a new coordinator, a new connection, the same files on disk.</summary>
    private SyncCoordinator Open()
    {
        var baseUrl = new Uri("https://api.crowdin.com/api/v2/");
        var factory = new SourceHttpClientFactory(new SourceNetworkPolicy(_manifest.Network, baseUrl), new StaticSourceCredentials(FakeCrowdin.Token),
            new ApiTokenAuth("token", "Token"), _diagnostics, new SourceHttpOptions { PrimaryHandler = () => _crowdin, MaxRetries = 0, LogRequests = true });
        _disposables.Add(factory);
        var context = new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = "crowdin", AdapterId = CrowdinAdapter.AdapterId, BaseUrl = baseUrl, RemoteProjectId = "7", Languages = new Dictionary<string, string> { ["fr"] = "fr" },
        }, http: factory, diagnostics: _diagnostics);
        var connections = Substitute.For<ISourceConnectionService>();
        connections.GetSessionAsync("crowdin", Arg.Any<CancellationToken>()).Returns(_ => _adapter.OpenSessionAsync(context, CancellationToken.None));
        var catalog = Substitute.For<ISourceAdapterCatalog>();
        catalog.Find(CrowdinAdapter.AdapterId).Returns(new RegisteredSourceAdapter("crowdin.connector", _adapter));
        var coordinator = new SyncCoordinator(_workspace, connections, catalog);
        coordinator.Open(_root, _settings);
        _disposables.Add(coordinator);
        return coordinator;
    }

    private static async Task<SyncRunResult> RunAsync(SyncCoordinator c, SyncPlanKind kind = SyncPlanKind.Sync) => await c.RunAsync("crowdin", kind);

    [Fact]
    public async Task ConnectEditOfflineRestartConflictPushAndReopen()
    {
        // 1. Connect and sync for the first time: both sides already agree, so nothing is conflicting and nothing is copied.
        var first = Open();
        var firstRun = await RunAsync(first);
        Assert.True(firstRun.Plan.IsFirstSync);
        Assert.Empty(firstRun.Plan.Conflicts);
        Assert.True(firstRun.Succeeded);
        Assert.Equal(2, new SyncStateStore(_root, "crowdin").LoadBaseline()!.Documents.Count);

        // 2. Someone translates in Crowdin: the next pull brings the file in.
        _file.Remote.Translations["fr"] = """{"title":"Bienvenue !"}"""u8.ToArray();
        var pulled = await RunAsync(first, SyncPlanKind.Pull);
        Assert.Equal(1, pulled.Pull!.Applied);
        Assert.Equal("""{"title":"Bienvenue !"}""", Read("locales/fr-FR/common.json"));

        // 3. Offline: edit the source file here. Then close Toucan.
        Write("locales/en-US/common.json", """{"title":"Welcome!"}""");
        first.Dispose();

        // 4. Reopen. Meanwhile the translator changed the same French file again, and so did we: a conflict nothing resolves silently.
        Write("locales/fr-FR/common.json", """{"title":"Mon texte"}""");
        _file.Remote.Translations["fr"] = """{"title":"Leur texte"}"""u8.ToArray();
        var second = Open();
        var plan = await second.PlanAsync("crowdin", SyncPlanKind.Sync);
        Assert.True(plan.IsDocumentPlan);
        Assert.Equal("locales/fr-FR/common.json", Assert.Single(plan.Conflicts).Remote.Name);
        Assert.Equal("locales/en-US/common.json", Assert.Single(plan.Push).Remote.Name);

        // 5. The non-conflicting source change goes out; the conflict stays until we choose.
        var partial = await second.ApplyAsync(plan);
        Assert.Equal(1, partial.Push!.Confirmed);
        Assert.Equal("""{"title":"Welcome!"}""", Encoding.UTF8.GetString(_file.Remote.Source));
        Assert.Equal("""{"title":"Leur texte"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));
        Assert.Equal("""{"title":"Mon texte"}""", Read("locales/fr-FR/common.json"));

        // 6. Keep ours.
        var again = await second.PlanAsync("crowdin", SyncPlanKind.Sync);
        var resolved = SyncPlanResolver.Resolve(again, again.Conflicts.Select(c => new SyncResolution(c.Id, SyncChoice.TakeLocal)));
        var pushed = await second.ApplyAsync(resolved);
        Assert.Equal(1, pushed.Push!.Confirmed);
        Assert.Equal("""{"title":"Mon texte"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));
        second.Dispose();

        // 7. Reopen once more: everything agrees.
        var third = Open();
        var quiet = await third.PlanAsync("crowdin", SyncPlanKind.Sync);
        Assert.False(quiet.HasChanges);
        Assert.Empty(quiet.Conflicts);

        // 8. The token appears nowhere a user could copy it from.
        Assert.DoesNotContain(_diagnostics.Lines, l => l.Message.Contains(FakeCrowdin.Token, StringComparison.Ordinal));
        Assert.NotEmpty(_diagnostics.Lines);
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(FakeCrowdin.Token, File.ReadAllText(file), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstSyncWithDifferentFilesNeedsAChoiceAndTakeRemoteOverwritesOnlyThatFile()
    {
        Write("locales/fr-FR/common.json", """{"title":"Salut"}""");
        Write("locales/other.json", "untouched");
        var coordinator = Open();

        var plan = await coordinator.PlanAsync("crowdin", SyncPlanKind.Sync);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(SyncConflictKind.FirstSync, conflict.Kind);

        var result = await coordinator.ApplyAsync(SyncPlanner.ResolveAll(plan, SyncChoice.TakeRemote));

        Assert.Equal(1, result.Pull!.Applied);
        Assert.Equal("""{"title":"Bienvenue"}""", Read("locales/fr-FR/common.json"));
        Assert.Equal("untouched", Read("locales/other.json"));
    }

    [Fact]
    public async Task ADocumentDeletedLocallyIsOnlyOfferedNotSent()
    {
        var coordinator = Open();
        await RunAsync(coordinator);
        File.Delete(Path.Combine(_root, "locales/fr-FR/common.json"));

        var plan = await coordinator.PlanAsync("crowdin", SyncPlanKind.Push);

        var delete = Assert.Single(plan.Push);
        Assert.Equal(PushKind.DeleteKey, delete.Kind);
        Assert.False(delete.Include);
        var result = await coordinator.ApplyAsync(plan);
        Assert.Equal(0, result.Push!.Confirmed);
        Assert.True(_file.Remote.Translations.ContainsKey("fr"));
    }

    // ───────────── opening a project from the service ─────────────

    private (RemoteProjectBootstrap Bootstrap, RegisteredSourceAdapter Adapter) Bootstrap()
    {
        var baseUrl = new Uri("https://api.crowdin.com/api/v2/");
        var factory = new SourceHttpClientFactory(new SourceNetworkPolicy(_manifest.Network, baseUrl), new StaticSourceCredentials(FakeCrowdin.Token),
            new ApiTokenAuth("token", "Token"), _diagnostics, new SourceHttpOptions { PrimaryHandler = () => _crowdin, MaxRetries = 0 });
        _disposables.Add(factory);
        return (new RemoteProjectBootstrap((settings, folder, ct) => _adapter.OpenSessionAsync(new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = settings.Id, AdapterId = settings.Adapter!, BaseUrl = baseUrl, RemoteProjectId = settings.RemoteProject!.Id,
            Languages = settings.Languages, Role = SourceRole.Primary,
        }, http: factory, diagnostics: _diagnostics), ct), null), new RegisteredSourceAdapter("crowdin.connector", _adapter));
    }

    [Fact]
    public async Task OpeningAProjectFromTheServiceCreatesTheFolderManifestAndFiles()
    {
        var (bootstrap, adapter) = Bootstrap();
        var folder = Path.Combine(_root, "remote", "crowdin", "demo");

        var result = await bootstrap.CreateAsync(adapter, null, new RemoteProjectSummary("7", "Demo"), folder);

        Assert.Equal(("crowdin:Demo", Path.GetFullPath(folder)), (result.Label, result.Folder));
        Assert.Equal(3, result.Documents); // the source file and its French and German exports
        Assert.Equal("""{"title":"Welcome"}""", File.ReadAllText(Path.Combine(folder, "locales/en-US/common.json")));
        Assert.True(File.Exists(Path.Combine(folder, "locales/fr-FR/common.json")));
        Assert.True(File.Exists(Path.Combine(folder, "locales/de-DE/common.json")));

        var settings = ProjectSettings.LoadFrom(folder)!;
        Assert.Equal(("en", "Demo"), (settings.PrimaryLanguage, settings.Name));
        Assert.Equal(["de", "en", "fr"], settings.Languages.Order());
        var connection = Assert.Single(settings.Sources);
        Assert.Equal((CrowdinAdapter.AdapterId, SourceRoles.Primary, "7", "crowdin"), (connection.Adapter, connection.Role, connection.RemoteProject!.Id, connection.Id));
        Assert.Null(connection.BaseUrl);
        Assert.Equal(3, new SyncStateStore(folder, "crowdin").LoadBaseline()!.Documents.Count);
    }

    [Fact]
    public async Task AFreshlyOpenedProjectHasNothingLeftToSync()
    {
        var (bootstrap, adapter) = Bootstrap();
        var folder = Path.Combine(_root, "fresh");
        var result = await bootstrap.CreateAsync(adapter, null, new RemoteProjectSummary("7", "Demo"), folder);

        // Opened as a project: the same files, the same connection.
        var settings = new List<SourceConnectionSettings> { result.Connection };
        var baseUrl = new Uri("https://api.crowdin.com/api/v2/");
        var factory = new SourceHttpClientFactory(new SourceNetworkPolicy(_manifest.Network, baseUrl), new StaticSourceCredentials(FakeCrowdin.Token),
            new ApiTokenAuth("token", "Token"), _diagnostics, new SourceHttpOptions { PrimaryHandler = () => _crowdin, MaxRetries = 0 });
        _disposables.Add(factory);
        var connections = Substitute.For<ISourceConnectionService>();
        connections.GetSessionAsync("crowdin", Arg.Any<CancellationToken>()).Returns(_ => _adapter.OpenSessionAsync(new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = "crowdin", AdapterId = CrowdinAdapter.AdapterId, BaseUrl = baseUrl, RemoteProjectId = "7", Languages = result.Connection.Languages,
        }, http: factory), CancellationToken.None));
        var catalog = Substitute.For<ISourceAdapterCatalog>();
        catalog.Find(CrowdinAdapter.AdapterId).Returns(new RegisteredSourceAdapter("crowdin.connector", _adapter));
        using var coordinator = new SyncCoordinator(_workspace, connections, catalog);
        coordinator.Open(folder, settings);

        var plan = await coordinator.PlanAsync("crowdin", SyncPlanKind.Sync);

        Assert.False(plan.HasChanges);
        Assert.False(plan.IsFirstSync);
    }

    [Fact]
    public async Task AFolderThatIsNotEmptyOrAlreadyAProjectIsRefusedBeforeAnythingIsWritten()
    {
        var (bootstrap, adapter) = Bootstrap();
        var busy = Path.Combine(_root, "busy");
        Directory.CreateDirectory(busy);
        File.WriteAllText(Path.Combine(busy, "keep.txt"), "mine");
        var project = new RemoteProjectSummary("7", "Demo");

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.CreateAsync(adapter, null, project, busy));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(busy, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(busy, "toucan.tproj")));
        Assert.Empty(_crowdin.Requests);

        var existing = Path.Combine(_root, "existing");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "toucan.tproj"), "{}");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.CreateAsync(adapter, null, project, existing));
        Assert.Contains("already a Toucan project", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServiceThatStoresKeysCannotOpenAProjectThisWay()
    {
        var (bootstrap, _) = Bootstrap();
        var unitAdapter = new RegisteredSourceAdapter("toucan", new UnitOnlyAdapter());
        var failure = await Assert.ThrowsAsync<NotSupportedException>(() => bootstrap.CreateAsync(unitAdapter, null, new RemoteProjectSummary("1", "P"), Path.Combine(_root, "units")));
        Assert.Contains("keys, not files", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "units")));
    }

    private sealed class UnitOnlyAdapter : IContentSourceAdapter
    {
        public SourceAdapterDescriptor Descriptor { get; } = new()
        {
            Id = "toucan.sources.units", DisplayName = "Units", DefaultBaseUrl = new Uri("https://x.test/"), AuthMethods = [new ApiTokenAuth("t", "T")], Capabilities = SourceCapabilities.ReadUnits,
        };
        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("My Project", "My-Project")]
    [InlineData("a/b:c", "a-b-c")]
    [InlineData("  ..  ", "project")]
    public void FolderNamesAreSafeOnEveryPlatform(string name, string expected) => Assert.Equal(expected, RemoteProjectBootstrap.FolderName(name));

    [Fact]
    public void TheLabelNamesTheServiceAndTheProject() =>
        Assert.Equal("crowdin:toucan-connector-test", RemoteProjectBootstrap.LabelFor(new RegisteredSourceAdapter("crowdin.connector", _adapter), "toucan-connector-test"));

    [Fact]
    public async Task FilesThatAreNotPartOfTheProjectAreNeverSentToTheService()
    {
        var coordinator = Open();
        await RunAsync(coordinator);
        // Only the project's own documents (and the service's paths) take part; an unrelated local file is not a new translation file.
        Write("locales/notes.txt", "private notes");

        var plan = await coordinator.PlanAsync("crowdin", SyncPlanKind.Push);

        Assert.DoesNotContain(plan.Push, p => p.Remote.Name.Contains("notes", StringComparison.Ordinal));
        Assert.DoesNotContain(_crowdin.Requests, r => r.Method == HttpMethod.Post && r.Url.AbsolutePath.EndsWith("/storages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APartialListingNeverDeletesAnything()
    {
        var coordinator = Open();
        await RunAsync(coordinator);
        var plan = await coordinator.PlanAsync("crowdin", SyncPlanKind.Pull);
        Assert.True(plan.RemoteReadComplete);
        Assert.DoesNotContain(plan.Pull, p => p.Kind == PullKind.Delete);
    }

    // ───────────── interrupted and partial pushes (V1-15) ─────────────

    private const string Source = "locales/en-US/common.json";
    private const string French = "locales/fr-FR/common.json";

    /// <summary>Source updates (PUT files/{id}) or translation imports (POST translations/{language}) sent so far.</summary>
    private int Imports(string pathPart) => _crowdin.Requests.Count(r =>
        r.Method == (pathPart.StartsWith("/files/", StringComparison.Ordinal) ? HttpMethod.Put : HttpMethod.Post) && r.Url.AbsolutePath.EndsWith(pathPart, StringComparison.Ordinal));

    private async Task<SyncCoordinator> SyncedWithTwoLocalEditsAsync()
    {
        var coordinator = Open();
        Assert.True((await RunAsync(coordinator)).Succeeded);
        Write(Source, """{"title":"Welcome!"}""");
        Write(French, """{"title":"Bienvenue !"}""");
        return coordinator;
    }

    [Fact]
    public async Task WhenOneFileFailsTheOtherIsRecordedAndOnlyTheFailedOneIsSentAgain()
    {
        var coordinator = await SyncedWithTwoLocalEditsAsync();
        _crowdin.FailNext = ("translations/fr", System.Net.HttpStatusCode.InternalServerError, null);

        var first = await coordinator.ApplyAsync(await coordinator.PlanAsync("crowdin", SyncPlanKind.Push));

        Assert.False(first.Succeeded);
        Assert.Equal(1, first.Push!.Confirmed);
        Assert.Equal(1, first.Push.Unknown);
        Assert.Equal("""{"title":"Welcome!"}""", Encoding.UTF8.GetString(_file.Remote.Source));
        Assert.Equal("""{"title":"Bienvenue"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));

        var retry = await coordinator.PlanAsync("crowdin", SyncPlanKind.Push);
        Assert.Equal([French], retry.Push.Select(c => c.Remote.Name));
        Assert.True((await coordinator.ApplyAsync(retry)).Succeeded);
        Assert.Equal("""{"title":"Bienvenue !"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));
        Assert.Equal(1, Imports("/files/" + _file.Remote.Id));
    }

    [Fact]
    public async Task APermissionErrorIsReportedAndTheFileStaysPending()
    {
        var coordinator = await SyncedWithTwoLocalEditsAsync();
        _crowdin.FailNext = ("translations/fr", System.Net.HttpStatusCode.Forbidden, null);

        var result = await coordinator.ApplyAsync(await coordinator.PlanAsync("crowdin", SyncPlanKind.Push));

        Assert.Equal(1, result.Push!.Confirmed);
        Assert.Equal(1, result.Push.Failed);
        Assert.Contains(result.Push.Errors, e => e.Contains(French, StringComparison.Ordinal));
        Assert.Equal("""{"title":"Bienvenue"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));
        Assert.Equal([French], (await coordinator.PlanAsync("crowdin", SyncPlanKind.Push)).Push.Select(c => c.Remote.Name));
    }

    [Fact]
    public async Task ATimeoutAfterCrowdinImportedTheFileIsConfirmedByReadingItBack()
    {
        var coordinator = await SyncedWithTwoLocalEditsAsync();
        Write(Source, """{"title":"Welcome"}""");
        _crowdin.DropAfterNextImport = true;

        var result = await coordinator.ApplyAsync(await coordinator.PlanAsync("crowdin", SyncPlanKind.Push));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Push!.Confirmed);
        var again = await coordinator.PlanAsync("crowdin", SyncPlanKind.Sync);
        Assert.False(again.HasChanges);
        Assert.Equal(1, Imports("/translations/fr"));
    }

    [Fact]
    public async Task ACrashAfterTheFirstUploadNeitherRepeatsItNorLosesTheSecond()
    {
        var coordinator = await SyncedWithTwoLocalEditsAsync();
        using var crash = new CancellationTokenSource();
        _crowdin.AfterImport = _ => crash.Cancel();

        // The process stops right after Crowdin applied the source upload, before Toucan recorded it.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ApplyAsync(coordinator.PlanAsync("crowdin", SyncPlanKind.Push).Result, crash.Token));
        _crowdin.AfterImport = null;
        coordinator.Dispose();
        Assert.Equal("""{"title":"Welcome!"}""", Encoding.UTF8.GetString(_file.Remote.Source));
        Assert.Equal("""{"title":"Bienvenue"}""", Encoding.UTF8.GetString(_file.Remote.Translations["fr"]));

        // Restart: the source already matches, so it converges; only French is still to send.
        var reopened = Open();
        var plan = await reopened.PlanAsync("crowdin", SyncPlanKind.Push);
        Assert.Empty(plan.Conflicts);
        Assert.Equal([French], plan.Push.Select(c => c.Remote.Name));
        Assert.True((await reopened.ApplyAsync(plan)).Succeeded);

        Assert.Equal(1, Imports("/files/" + _file.Remote.Id));
        Assert.Equal(1, Imports("/translations/fr"));
        Assert.False((await reopened.PlanAsync("crowdin", SyncPlanKind.Sync)).HasChanges);
    }

    [Fact]
    public async Task ACrashAfterCrowdinReformattedTheUploadIsAConflictNotASilentOverwrite()
    {
        var coordinator = await SyncedWithTwoLocalEditsAsync();
        Write(Source, """{"title":"Welcome"}""");
        _crowdin.NormalizeImport = bytes => [.. bytes, (byte)'\n'];
        using var crash = new CancellationTokenSource();
        _crowdin.AfterImport = _ => crash.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ApplyAsync(coordinator.PlanAsync("crowdin", SyncPlanKind.Push).Result, crash.Token));
        _crowdin.AfterImport = null;
        coordinator.Dispose();

        // Without a record of the upload, a reformatted export cannot be told from someone else's edit: the user chooses.
        var plan = await Open().PlanAsync("crowdin", SyncPlanKind.Push);
        Assert.Equal(French, Assert.Single(plan.Conflicts).Remote.Name);
        Assert.Empty(plan.Push);
        Assert.Equal(1, Imports("/translations/fr"));
    }
}
