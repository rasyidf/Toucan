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
        Write("locales/en/common.json", """{"title":"Welcome"}""");
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
        Write("locales/en/common.json", """{"title":"Welcome!"}""");
        first.Dispose();

        // 4. Reopen. Meanwhile the translator changed the same French file again, and so did we: a conflict nothing resolves silently.
        Write("locales/fr-FR/common.json", """{"title":"Mon texte"}""");
        _file.Remote.Translations["fr"] = """{"title":"Leur texte"}"""u8.ToArray();
        var second = Open();
        var plan = await second.PlanAsync("crowdin", SyncPlanKind.Sync);
        Assert.True(plan.IsDocumentPlan);
        Assert.Equal("locales/fr-FR/common.json", Assert.Single(plan.Conflicts).Remote.Name);
        Assert.Equal("locales/en/common.json", Assert.Single(plan.Push).Remote.Name);

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
}
