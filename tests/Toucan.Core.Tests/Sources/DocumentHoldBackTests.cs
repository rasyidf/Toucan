using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Modules;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>
/// Strict validation (<c>requireValidForApproval</c>) for connections that sync whole files: a file holding an invalid value
/// is not uploaded, because a file cannot be sent without it. The check follows the saved file, not unsaved edits.
/// </summary>
public sealed class DocumentHoldBackTests : IDisposable
{
    private const string French = "locales/fr/common.json";
    private const string English = "locales/en/common.json";
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-document-holdback-").FullName;
    private readonly ServiceProvider _services;
    private readonly MemoryDocumentSession _remote = new() { PageSize = 10 };
    private readonly List<IDisposable> _disposables = [];

    public DocumentHoldBackTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _remote.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _services.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<(WorkspaceService Workspace, SyncCoordinator Sync)> OpenAsync(bool strict)
    {
        Write(English, "{\"title\":\"Hello {name}\"}");
        Write(French, "{\"title\":\"Bonjour {name}\"}");
        _remote.Put(English, "{\"title\":\"Hello {name}\"}");
        _remote.Put(French, "{\"title\":\"Bonjour {name}\"}");
        new ProjectSettings
        {
            ProjectPath = _root, Framework = "i18next", SaveFormat = FormatIds.Namespaced, PrimaryLanguage = "en", Languages = ["en", "fr"],
            RequireValidForApproval = strict,
        }.Save();
        var loaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        var workspace = _services.GetRequiredService<WorkspaceService>();
        workspace.Open(loaded.Settings, loaded.Source!);

        var connections = Substitute.For<ISourceConnectionService>();
        connections.GetSessionAsync("files", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IContentSourceSession>(_remote));
        var adapter = Substitute.For<IContentSourceAdapter>();
        adapter.Descriptor.Returns(new SourceAdapterDescriptor
        {
            Id = "test.files", DisplayName = "Files", AuthMethods = [], Capabilities = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments,
        });
        var catalog = Substitute.For<ISourceAdapterCatalog>();
        catalog.Find("test.files").Returns(new RegisteredSourceAdapter("test", adapter));
        var sync = new SyncCoordinator(workspace, connections, catalog);
        _disposables.Add(sync);
        sync.Open(_root, [new SourceConnectionSettings { Id = "files", Name = "Files", Adapter = "test.files", Languages = { ["fr"] = "fr" } }]);
        Assert.True((await sync.RunAsync("files", SyncPlanKind.Sync)).Succeeded);
        return (workspace, sync);
    }

    private static async Task SetFrenchAsync(WorkspaceService workspace, string value)
    {
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        var fr = snapshot.Units.Single(u => u.Language == "fr");
        Assert.True((await workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision, [new SetEntryValue(fr.EntryId, "fr", value)]))).Applied);
    }

    private Task SaveAsync(WorkspaceService workspace) =>
        _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(workspace.Source!);

    private static async Task SetEnglishAsync(WorkspaceService workspace, string value)
    {
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        var en = snapshot.Units.Single(u => u.Language == "en");
        Assert.True((await workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision, [new SetEntryValue(en.EntryId, "en", value)]))).Applied);
    }

    [Fact]
    public async Task AFileWithAnInvalidSavedValueIsHeldAndOtherFilesGoOut()
    {
        var (workspace, sync) = await OpenAsync(strict: true);
        await SetFrenchAsync(workspace, "Bonjour {nom");
        await SetEnglishAsync(workspace, "Hi {name}");
        await SaveAsync(workspace);

        var plan = await sync.PlanAsync("files", SyncPlanKind.Push);
        Assert.Equal([English], plan.Push.Select(c => c.Remote.Name));
        var notice = Assert.Single(plan.Notices, n => n.Kind == SyncNoticeKind.HeldBackInvalid);
        Assert.Contains(French, notice.Message);
        Assert.Contains("title [fr]", notice.Message);

        var run = await sync.ApplyAsync(plan);
        Assert.Equal(1, run.Push!.Confirmed);
        Assert.Equal("{\"title\":\"Bonjour {name}\"}", _remote.Text(French));
    }

    [Fact]
    public async Task AnUnsavedFixDoesNotReleaseTheSavedInvalidFile()
    {
        var (workspace, sync) = await OpenAsync(strict: true);
        await SetFrenchAsync(workspace, "Bonjour {nom");
        await SaveAsync(workspace);
        await SetFrenchAsync(workspace, "Bonjour {name} !");

        var held = await sync.PlanAsync("files", SyncPlanKind.Push);
        Assert.Empty(held.Push);
        Assert.Contains(held.Notices, n => n.Kind == SyncNoticeKind.HeldBackInvalid);

        await SaveAsync(workspace);
        var released = await sync.PlanAsync("files", SyncPlanKind.Push);
        Assert.Equal([French], released.Push.Select(c => c.Remote.Name));
        Assert.DoesNotContain(released.Notices, n => n.Kind == SyncNoticeKind.HeldBackInvalid);
    }

    [Fact]
    public async Task WithoutStrictValidationTheInvalidFileGoesOut()
    {
        var (workspace, sync) = await OpenAsync(strict: false);
        await SetFrenchAsync(workspace, "Bonjour {nom");
        await SaveAsync(workspace);

        var plan = await sync.PlanAsync("files", SyncPlanKind.Push);
        Assert.Equal([French], plan.Push.Select(c => c.Remote.Name));
    }
}
