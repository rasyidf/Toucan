using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceSourceLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-source-life-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly WorkspaceSourceLoader _loader;
    public WorkspaceSourceLifecycleTests()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults(); _services = services.BuildServiceProvider();
        _workspace = _services.GetRequiredService<WorkspaceService>(); _loader = _services.GetRequiredService<WorkspaceSourceLoader>();
    }
    private void Write(string path, string content) { path = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
    private async Task<WorkspaceSource> Open()
    {
        var source = await _loader.LoadAsync(_root, "i18next"); _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en" }, source); return source;
    }
    [Fact]
    public async Task CleanDocumentReloadPreservesDirtyDocumentAndItsUndoHistory()
    {
        Write("locales/en/a.json", "{\"title\":\"A\"}"); Write("locales/en/b.json", "{\"title\":\"B\"}"); await Open(); var session = _workspace.SessionId;
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("a.title", "en", "Mine"));
        Write("locales/en/b.json", "{\"title\":\"External\"}");
        var refresh = _workspace.ReloadSource(await _loader.LoadAsync(_root, "i18next"));
        Assert.Single(refresh.Reloaded); Assert.Empty(refresh.Conflicts); Assert.Equal(session, _workspace.SessionId);
        Assert.Equal("Mine", (await _workspace.SnapshotAsync())!.Find("a.title", "en")!.Value);
        Assert.Equal("External", (await _workspace.SnapshotAsync())!.Find("b.title", "en")!.Value);
        Assert.True(_workspace.Undo()); Assert.False(_workspace.IsDirty);
        Assert.Equal("External", (await _workspace.SnapshotAsync())!.Find("b.title", "en")!.Value);
    }
    [Fact]
    public async Task DirtyDocumentConflictRetainsItsOriginalSourceVersion()
    {
        Write("locales/en/a.json", "{\"title\":\"A\"}"); Write("locales/en/b.json", "{\"title\":\"B\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("a.title", "en", "Mine"));
        Write("locales/en/a.json", "{\"title\":\"Theirs\"}"); Write("locales/en/b.json", "{\"title\":\"External\"}");
        var result = _workspace.ReloadSource(await _loader.LoadAsync(_root, "i18next"));
        Assert.Single(result.Conflicts); Assert.Single(result.Reloaded);
        Assert.Equal(source.Documents.Single(d => d.Package == "a").Version, _workspace.Source!.Documents.Single(d => d.Package == "a").Version);
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(_workspace.Source));
        Assert.Equal("Mine", (await _workspace.SnapshotAsync())!.Find("a.title", "en")!.Value);
    }
    [Fact]
    public async Task IdentityAwareDraftRestoresRenameMetadataAndUndoAsOneTransaction()
    {
        Write("locales/en/common.json", "{\"a.b\":\"Literal\",\"a\":{\"b\":\"Nested\"}}"); var source = await Open();
        var snapshot = (await _workspace.SnapshotEntriesAsync())!; var literal = snapshot.Units.Single(u => u.Value == "Literal");
        await _workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision, [new RenameEntry(literal.EntryId, literal.Namespace, new(["renamed"])),
            new SetEntryValue(literal.EntryId, "en", "Recovered"), new SetEntryComment(literal.EntryId, "en", "Comment")]));
        var draft = new RecoveryDraft(_root, DateTime.UtcNow, []) { SchemaVersion = 2, Workspace = _workspace.CaptureRecovery(source) };
        var drafts = new RecoveryDraftService(Path.Combine(_root, "drafts")); drafts.Write(_root, draft);
        var loaded = drafts.TryRead(_root)!; Assert.Equal(1, loaded.ChangeCount);
        _workspace.Close(); await Open();
        var applied = _workspace.ApplyRecovery(loaded.Workspace!); Assert.Equal(1, applied.Applied); Assert.Empty(applied.Conflicts);
        var recovered = (await _workspace.SnapshotEntriesAsync())!.Units.Single(u => u.EntryId == literal.EntryId);
        Assert.Equal("Recovered", recovered.Value); Assert.Equal("Comment", recovered.Comment); Assert.Equal(["renamed"], recovered.KeyPath.Segments);
        Assert.True(_workspace.Undo()); Assert.False(_workspace.IsDirty);
    }
    [Fact]
    public async Task RecoveryCannotOverwriteAnExternalChangeOrCrossWorkspaceRoots()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", "Mine")); var draft = _workspace.CaptureRecovery(source);
        _workspace.Close(); Write("locales/en/common.json", "{\"title\":\"External\"}"); await Open();
        var result = _workspace.ApplyRecovery(draft); Assert.Single(result.Conflicts); Assert.Equal(0, result.Applied);
        Assert.Equal("External", (await _workspace.SnapshotAsync())!.Find("common.title", "en")!.Value);
        Assert.Throws<InvalidOperationException>(() => _workspace.ApplyRecovery(new(_root + "-other", draft.FrameworkId, draft.FormatId, draft.Baseline, draft.Units, draft.IdentityCheckpoint)));
    }
    [Fact]
    public async Task ExportIsCreateOnlyAndLeavesOriginalWorkspaceAndFilesUnchanged()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); var source = await Open(); var session = _workspace.SessionId;
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", "Exported"));
        var destination = _root + "-copy";
        try
        {
            var planner = _services.GetRequiredService<WorkspaceExportPlanner>(); var plan = planner.Plan(source, _workspace.CaptureDocumentSnapshot()!, destination);
            Assert.All(plan.Targets, t => { Assert.Equal(WorkspaceDocumentSaveOperation.Create, t.Operation); Assert.Null(t.ExpectedVersion); });
            await _services.GetRequiredService<WorkspaceDocumentSaveExecutor>().ExecuteAsync(plan);
            Assert.Equal(session, _workspace.SessionId); Assert.True(_workspace.IsDirty);
            Assert.Contains("Before", File.ReadAllText(Path.Combine(_root, "locales/en/common.json")), StringComparison.Ordinal);
            Assert.Contains("Exported", File.ReadAllText(Path.Combine(destination, "locales/en/common.json")), StringComparison.Ordinal);
            await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => _services.GetRequiredService<WorkspaceDocumentSaveExecutor>().ExecuteAsync(plan));
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    }
    [Fact]
    public async Task BothNavigatorsDriveTheSameIdentifiedEditAndSaveTargets()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); var source = await Open(); var navigator = _services.GetRequiredService<WorkspaceNavigator>();
        var snapshot = (await _workspace.SnapshotEntriesAsync())!; var id = snapshot.Units.Single().EntryId;
        var a = navigator.Build(snapshot, source, WorkspaceNavigationLayout.NamespaceFirst); var b = navigator.Build(snapshot, source, WorkspaceNavigationLayout.FileFirst);
        Assert.Equal(a.RetainSelection([id]), b.RetainSelection([id]));
        await _workspace.ApplyEntriesAsync("table-presenter", new(snapshot.SessionId, snapshot.Revision, [new SetEntryValue(id, "en", "Changed")]));
        var first = _services.GetRequiredService<WorkspaceDocumentSavePlanner>().Plan(source, _workspace.CaptureDocumentSnapshot()!);
        _workspace.Undo(); var current = (await _workspace.SnapshotEntriesAsync())!;
        await _workspace.ApplyEntriesAsync("tree-presenter", new(current.SessionId, current.Revision, [new SetEntryValue(b.RetainSelection([id]).Single(), "en", "Changed")]));
        var second = _services.GetRequiredService<WorkspaceDocumentSavePlanner>().Plan(source, _workspace.CaptureDocumentSnapshot()!);
        Assert.Equal(first.Targets.Select(t => (t.RelativePath, t.Content)), second.Targets.Select(t => (t.RelativePath, t.Content)));
        Assert.Equal(first.Artifacts.Select(t => t.RelativePath), second.Artifacts.Select(t => t.RelativePath));
    }
    [Fact]
    public async Task DurableCatalogTakesPrecedenceOverRecoveryFallback()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().RenameKey("common.title", "common.renamed"));
        await _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(source);
        var saved = _workspace.Source!;
        var fallback = new WorkspaceIdentityCheckpoint(_root, saved.IdentityCatalog.Select(e => e with { Id = new(Guid.NewGuid()) }).ToArray());
        var reloaded = await _loader.LoadAsync(_root, "i18next", fallbackIdentityCheckpoint: fallback);
        Assert.Equal(saved.Units.Select(u => u.EntryId), reloaded.Units.Select(u => u.EntryId));
    }

    [Fact]
    public async Task DraftIdentityFallbackCannotCrossRootsOrOverwriteExternalContent()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); var discovered = await _loader.LoadAsync(_root, "i18next");
        var checkpoint = new WorkspaceIdentityCheckpoint(_root, discovered.IdentityCatalog.Select(e => e with { Id = new(Guid.NewGuid()) }).ToArray());
        var source = await _loader.LoadAsync(_root, "i18next", fallbackIdentityCheckpoint: checkpoint);
        _workspace.Open(new() { ProjectPath = _root }, source);
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", "Mine"));
        var draft = _workspace.CaptureRecovery(source); _workspace.Close();
        Write("locales/en/common.json", "{\"title\":\"External\"}");
        var incoming = await _loader.LoadAsync(_root, "i18next", fallbackIdentityCheckpoint: draft.IdentityCheckpoint);
        _workspace.Open(new() { ProjectPath = _root }, incoming);
        var recovery = _workspace.ApplyRecovery(draft); Assert.Equal(0, recovery.Applied); Assert.Single(recovery.Conflicts);
        Assert.Equal("External", (await _workspace.SnapshotAsync())!.Find("common.title", "en")!.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _loader.LoadAsync(_root, "i18next", fallbackIdentityCheckpoint: checkpoint with { Root = _root + "-other" }));
    }

    [Fact]
    public async Task DeepNavigationPathsHaveBoundedStableNodeIdentities()
    {
        Write("locales/en/common.json", "{\"title\":\"Before\"}"); await Open();
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var unit = snapshot.Units.Single() with { Namespace = TranslationPath.Empty,
            KeyPath = new(Enumerable.Range(0, 40).Select(i => "segment" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()) };
        snapshot = snapshot with { Units = [unit] };
        var navigator = _services.GetRequiredService<WorkspaceNavigator>();
        var first = navigator.Build(snapshot, null, WorkspaceNavigationLayout.NamespaceFirst);
        var second = navigator.Build(snapshot, null, WorkspaceNavigationLayout.NamespaceFirst);
        var left = Assert.Single(first.Roots); var right = Assert.Single(second.Roots);
        for (var depth = 0; depth < 40; depth++)
        {
            Assert.InRange(left.Id.Length, 1, 128); Assert.Equal(left.Id, right.Id);
            if (depth == 39) { Assert.Empty(left.Children); Assert.Equal(WorkspaceNavigationNodeKind.Entry, left.Kind); }
            else { left = Assert.Single(left.Children); right = Assert.Single(right.Children); }
        }
        Assert.Equal(unit.EntryId, Assert.Single(left.EntryIds));
    }

    [Fact]
    public void CapabilityResolutionIsExplicitAndReportsLegacyBulkFormats()
    {
        var resolver = _services.GetRequiredService<WorkspaceCapabilityResolver>();
        Assert.True(resolver.Resolve("i18next").UsesDocumentPersistence);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("missing"));
        Assert.Throws<FormatUnavailableException>(() => resolver.Resolve("i18next", "missing.format"));
        Assert.Equal("LegacyBulk", resolver.Resolve("i18next", FormatIds.Yaml).SaveScope);
    }
    public void Dispose() { _workspace.Dispose(); _services.Dispose(); Directory.Delete(_root, true); }
}
