using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceMetadataTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-metadata-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly WorkspaceSourceLoader _loader;
    private readonly WorkspaceDocumentSaveCoordinator _save;
    public WorkspaceMetadataTests()
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _loader = _services.GetRequiredService<WorkspaceSourceLoader>();
        _save = _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>();
    }
    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
    }
    private async Task<WorkspaceSource> Open()
    {
        var source = await _loader.LoadAsync(_root, "i18next");
        _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en" }, source); return source;
    }
    [Fact]
    public async Task RenameCommentAndReviewReopenWithTheSameIdentity()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open(); var before = (await _workspace.SnapshotEntriesAsync())!; var unit = before.Units.Single();
        await _workspace.ApplyEntriesAsync("editor", new(before.SessionId, before.Revision, [new RenameEntry(unit.EntryId, unit.Namespace, new(["renamed"])),
            new SetEntryComment(unit.EntryId, "en", "Comment"), new SetEntryReview(unit.EntryId, "en", ReviewState.Approved)]));
        var saved = await _save.SaveAsync(source);
        Assert.Equal(2, saved.Receipt.Plan.Artifacts.Count);
        var reloaded = await Open(); var reopened = (await _workspace.SnapshotEntriesAsync())!.Units.Single();
        Assert.Equal(unit.EntryId, reopened.EntryId); Assert.Equal("Comment", reopened.Comment); Assert.Equal(ReviewState.Approved, reopened.Review);
        Assert.Equal(["renamed"], reopened.KeyPath.Segments); Assert.False(_workspace.IsDirty);
        Assert.Equal(2, reloaded.Artifacts.Count);
    }
    [Fact]
    public async Task CommentOnlySaveLeavesBothDocumentsAndUnrelatedMetadataUntouched()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}"); Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        var source = await Open(); await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "fr", "Français"));
        var saved = await _save.SaveAsync(source);
        var untouched = saved.Receipt.Source.Artifacts.Single(); var timestamp = File.GetLastWriteTimeUtc(Path.Combine(_root, untouched.RelativePath));
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "en", "English"));
        var next = await _save.SaveAsync(saved.Receipt.Source);
        Assert.Empty(next.Receipt.Plan.Targets); Assert.Single(next.Receipt.Plan.Artifacts);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(_root, untouched.RelativePath)));
        Assert.Equal(untouched.Version, SaveTransaction.Fingerprint([Path.Combine(_root, untouched.RelativePath)])[Path.Combine(_root, untouched.RelativePath)]);
        var noop = await _save.SaveAsync(next.Receipt.Source); Assert.Empty(noop.Receipt.Plan.Targets); Assert.Empty(noop.Receipt.Plan.Artifacts);
    }
    [Fact]
    public async Task ClearingAnnotationsDeletesOnlyTheirOwnedSidecar()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "en", "Comment"));
        var saved = await _save.SaveAsync(source); var artifact = saved.Receipt.Source.Artifacts.Single();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().DeleteKey("common.title"));
        var next = await _save.SaveAsync(saved.Receipt.Source);
        Assert.Contains(next.Receipt.Plan.Artifacts, a => a.RelativePath == artifact.RelativePath && a.Operation == WorkspaceDocumentSaveOperation.Delete);
        Assert.False(File.Exists(Path.Combine(_root, artifact.RelativePath))); Assert.False(_workspace.IsDirty);
    }
    [Fact]
    public async Task MetadataConflictRefusesTheWholeSaveBeforeDocumentWrites()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "en", "Comment"));
        var saved = await _save.SaveAsync(source); var artifact = saved.Receipt.Source.Artifacts.Single();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", "Changed").SetComment("common.title", "en", "Next"));
        File.AppendAllText(Path.Combine(_root, artifact.RelativePath), " ");
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => _save.SaveAsync(saved.Receipt.Source));
        Assert.Contains("Hello", File.ReadAllText(Path.Combine(_root, "locales/en/common.json")), StringComparison.Ordinal); Assert.True(_workspace.IsDirty);
    }
    [Fact]
    public async Task UnknownMetadataSchemaIsRefusedWithoutRewritingTheFile()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}"); var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "en", "Comment"));
        var saved = await _save.SaveAsync(source); var artifact = saved.Receipt.Source.Artifacts.Single();
        var content = artifact.Content.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99", StringComparison.Ordinal); Write(artifact.RelativePath, content);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _loader.LoadAsync(_root, "i18next"));
        Assert.Equal(content, File.ReadAllText(Path.Combine(_root, artifact.RelativePath)));
    }
    [Theory]
    [InlineData(WorkspaceNavigationLayout.NamespaceFirst)]
    [InlineData(WorkspaceNavigationLayout.FileFirst)]
    public async Task NavigationUsesLiteralSegmentsAndStableEntrySelection(WorkspaceNavigationLayout layout)
    {
        Write("locales/en/common.json", "{\"a.b\":\"Literal\",\"a\":{\"b\":\"Nested\"}}"); Write("locales/en/other.json", "{\"a.b\":\"Other\"}");
        var source = await Open(); var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var navigator = _services.GetRequiredService<WorkspaceNavigator>(); var projection = navigator.Build(snapshot, source, layout);
        var literal = snapshot.Units.Single(u => u.Value == "Literal"); Assert.Equal([literal.EntryId], projection.RetainSelection([literal.EntryId, Guid.NewGuid()]));
        Assert.Equal(snapshot.Units.Select(u => u.EntryId).Order(), projection.Roots.SelectMany(n => n.EntryIds).Distinct().Order());
        var leaves = Flatten(projection.Roots).Where(n => n.Kind == WorkspaceNavigationNodeKind.Entry).ToList();
        Assert.Contains(leaves, n => n.Label == "a.b" && n.EntryIds.Contains(literal.EntryId));
        Assert.All(leaves, n => Assert.All(n.Origins, origin => Assert.StartsWith("locales/", origin, StringComparison.Ordinal)));
        var other = navigator.Build(snapshot, source, layout == WorkspaceNavigationLayout.FileFirst ? WorkspaceNavigationLayout.NamespaceFirst : WorkspaceNavigationLayout.FileFirst);
        Assert.Equal([literal.EntryId], other.RetainSelection(projection.RetainSelection([literal.EntryId])));
    }
    private static IEnumerable<WorkspaceNavigationNode> Flatten(IEnumerable<WorkspaceNavigationNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
    public void Dispose() { _workspace.Dispose(); _services.Dispose(); Directory.Delete(_root, true); }
}
