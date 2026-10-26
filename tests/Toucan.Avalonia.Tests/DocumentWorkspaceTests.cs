using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class DocumentWorkspaceTests
{
    private static string Project(TestHost host)
    {
        var root = Path.Combine(host.Root, "documents");
        Write(root, "locales/en/common.json", "{\"title\":\"Hello\",\"onlyEn\":\"English\"}");
        Write(root, "locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        Write(root, "locales/en/other.json", "{\"title\":\"Other\"}");
        new ProjectSettings { ProjectPath = root, Framework = "i18next", SaveFormat = FormatIds.Namespaced, PrimaryLanguage = "en", Languages = ["en", "fr"] }.Save();
        return root;
    }
    private static void Write(string root, string relative, string content)
    { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }

    private static IEnumerable<Toucan.Avalonia.ViewModels.WorkspaceNavigationItemViewModel> Nodes(IEnumerable<Toucan.Avalonia.ViewModels.WorkspaceNavigationItemViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Nodes(root.Children)) yield return child;
        }
    }

    [AvaloniaFact]
    public async Task NavigatorAndEditorLayoutSwitchesFlushTextAndRetainEntrySelection()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var engine = host.Services.GetRequiredService<WorkspaceService>(); var session = engine.SessionId;
        var entry = (await engine.SnapshotEntriesAsync())!.Units.Single(u => u.Key == "common.title" && u.Language == "en");
        Assert.True(vm.HasDocumentNavigation);
        var explorer = new Toucan.Avalonia.Views.Panels.ExplorerPanel { DataContext = vm };
        vm.SetNavigationLayoutCommand.Execute(WorkspaceNavigationLayout.FileFirst);
        Assert.Contains(vm.DocumentNavigationItems, n => n.Label == "locales/en/common.json");
        Assert.Same(vm.DocumentNavigationItems, explorer.FindControl<TreeView>("DocumentTree")!.ItemsSource);
        vm.SelectedDocumentNode = Nodes(vm.DocumentNavigationItems).First(n => n.IsEntry && n.Node.EntryIds.Contains(entry.EntryId));
        var group = Assert.Single(vm.PagingController.Data); Assert.Equal(entry.EntryId, group.EntryId);
        group.Translations.Single(t => t.Language == "en").Value = "Pending before switch";
        vm.SetNavigationLayoutCommand.Execute(WorkspaceNavigationLayout.NamespaceFirst);
        Assert.Equal(entry.EntryId, vm.SelectedGroup!.EntryId);
        Assert.Equal("Pending before switch", (await engine.SnapshotAsync())!.Find("common.title", "en")!.Value);
        vm.SetEditorLayoutCommand.Execute(Toucan.Avalonia.ViewModels.EditorLayout.Rows);
        vm.SetEditorLayoutCommand.Execute(Toucan.Avalonia.ViewModels.EditorLayout.Focused);
        Assert.Equal(entry.EntryId, vm.SelectedGroup!.EntryId); Assert.Equal(session, engine.SessionId);
        Assert.Contains("locales/en/common.json", vm.SelectedGroup.SourceOrigins, StringComparison.Ordinal);
        var other = Nodes(vm.DocumentNavigationItems).Single(n => n.IsEntry && n.Label == "onlyEn");
        vm.SelectedDocumentNode = other;
        Assert.Equal("common.onlyEn", Assert.Single(vm.PagingController.Data).Namespace);
        var originalFrench = File.ReadAllText(Path.Combine(root, "locales/fr/common.json"));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.Equal(originalFrench, File.ReadAllText(Path.Combine(root, "locales/fr/common.json")));
        Assert.Contains("Pending before switch", File.ReadAllText(Path.Combine(root, "locales/en/common.json")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DuplicateDisplayKeysStaySeparateAndRenameOnlyTheSelectedStructuredEntry()
    {
        using var host = new TestHost(); var root = Project(host);
        Write(root, "locales/en/common.json", "{\"a.b\":\"Literal\",\"a\":{\"b\":\"Nested\"}}");
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var engine = host.Services.GetRequiredService<WorkspaceService>();
        var literal = (await engine.SnapshotEntriesAsync())!.Units.Single(u => u.Value == "Literal");
        Assert.Equal(2, vm.PagingController.Data.Count(g => g.Namespace == "common.a.b"));
        vm.SelectedDocumentNode = Nodes(vm.DocumentNavigationItems).Single(n => n.IsEntry && n.Node.EntryIds.Contains(literal.EntryId));
        var card = Assert.Single(vm.PagingController.Data); Assert.Equal(literal.EntryId, card.EntryId);
        Assert.Equal("a.b", card.ShortName); Assert.Equal("common", card.ParentPath);
        card.Translations.Single().Value = "Literal edited";
        Assert.Null(vm.RenameItemCore(new() { Name = "a.b", Namespace = "common.a.b" }, "renamed.key"));
        var snapshot = (await engine.SnapshotEntriesAsync())!;
        Assert.Equal("Literal edited", snapshot.Units.Single(u => u.EntryId == literal.EntryId).Value);
        Assert.Equal(["renamed.key"], snapshot.Units.Single(u => u.EntryId == literal.EntryId).KeyPath.Segments);
        Assert.Contains(snapshot.Units, u => u.KeyPath.Segments.SequenceEqual(new[] { "a", "b" }) && u.Value == "Nested");
        Assert.Equal(literal.EntryId, vm.SelectedGroup!.EntryId);
        Assert.Equal("renamed.key", vm.SelectedGroup.ShortName); Assert.Equal("common", vm.SelectedGroup.ParentPath);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "locales/en/common.json")));
        Assert.Equal("Literal edited", json.RootElement.GetProperty("renamed.key").GetString());
        Assert.Equal("Nested", json.RootElement.GetProperty("a").GetProperty("b").GetString());
    }

    [AvaloniaFact]
    public async Task DocumentLanguageChangesCannotRunTheLegacyFileWriter()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
        await vm.AddLanguageAsync("de");
        await vm.DeleteLanguageCommand.ExecuteAsync(new SummaryItem { Language = "fr" });
        Assert.DoesNotContain(vm.AllTranslation, t => t.Language == "de");
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, content) in before) Assert.Equal(content, File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task DesktopSaveWritesOnlyTheEditedDocumentAndOwnedMetadata()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var engine = host.Services.GetRequiredService<WorkspaceService>(); Assert.NotNull(engine.Source);
        Assert.DoesNotContain(vm.AllTranslation, t => t.Namespace == "common.onlyEn" && t.Language == "fr");
        var untouched = new[] { "locales/fr/common.json", "locales/en/other.json", "toucan.tproj" }.ToDictionary(p => p, p =>
            (File.ReadAllText(Path.Combine(root, p)), File.GetLastWriteTimeUtc(Path.Combine(root, p))));
        var row = vm.PagingController.Data.Single(g => g.Namespace == "common.title").Translations.Single(t => t.Language == "en");
        row.Value = "Changed";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty); Assert.False(engine.IsDirty);
        Assert.Contains("Changed", File.ReadAllText(Path.Combine(root, "locales/en/common.json")), StringComparison.Ordinal);
        foreach (var (path, original) in untouched)
        { Assert.Equal(original.Item1, File.ReadAllText(Path.Combine(root, path))); Assert.Equal(original.Item2, File.GetLastWriteTimeUtc(Path.Combine(root, path))); }
        Assert.False(File.Exists(Path.Combine(root, "en.json")));
    }
    [AvaloniaFact]
    public async Task TargetedSaveDoesNotAcknowledgeAnUnrelatedExternalEdit()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var watcher = host.Services.GetRequiredService<IFileWatcherService>();
        Write(root, "locales/fr/common.json", "{\"title\":\"External\"}");
        var row = vm.PagingController.Data.Single(g => g.Namespace == "common.title").Translations.Single(t => t.Language == "en");
        row.Value = "Saved";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        var change = Assert.Single(watcher.GetChanges());
        Assert.Equal("locales/fr/common.json", change.RelativePath);
        Assert.Contains("External", File.ReadAllText(Path.Combine(root, change.RelativePath)), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DesktopRenameAndReviewRestoreIdentitiesAfterReopen()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var engine = host.Services.GetRequiredService<WorkspaceService>(); var before = (await engine.SnapshotEntriesAsync())!;
        var unit = before.Units.Single(u => u.Key == "common.title" && u.Language == "en");
        Assert.True(engine.ApplyEntries("editor", new(before.SessionId, before.Revision, [new RenameEntry(unit.EntryId, unit.Namespace, new(["renamed"])),
            new SetEntryComment(unit.EntryId, "en", "Comment"), new SetEntryReview(unit.EntryId, "en", ReviewState.Approved)])).Applied);
        var lifecycle = host.Services.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectSaveStatus.Success, (await lifecycle.SaveProjectAsync()).Status);
        Assert.Equal(CloseResult.Closed, await lifecycle.CloseProjectAsync());
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(root)).Status);
        var reopened = (await engine.SnapshotEntriesAsync())!.Units.Single(u => u.EntryId == unit.EntryId && u.Language == "en");
        Assert.Equal("Comment", reopened.Comment); Assert.Equal(ReviewState.Approved, reopened.Review); Assert.Equal(["renamed"], reopened.KeyPath.Segments);
    }
    [AvaloniaFact]
    public async Task SaveAsCopiesTheDeclaredLayoutAndStartsANewSession()
    {
        using var host = new TestHost(); var root = Project(host); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        var engine = host.Services.GetRequiredService<WorkspaceService>(); var session = engine.SessionId;
        await using var old = (await engine.SubscribeAsync())!;
        var target = Path.Combine(host.Root, "copy");
        var lifecycle = host.Services.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectSaveStatus.Success, (await lifecycle.SaveProjectAsAsync(target)).Status);
        Assert.Equal(target, engine.ProjectPath); Assert.NotEqual(session, engine.SessionId);
        Assert.True(File.Exists(Path.Combine(target, "locales/fr/common.json"))); Assert.False(File.Exists(Path.Combine(target, "fr.json")));
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(target)).Status); Assert.NotNull(engine.Source);
        var events = new List<WorkspaceChange>(); await foreach (var change in old.Changes) events.Add(change);
        Assert.Equal(WorkspaceChangeKind.Closed, events[^1].Kind);
    }
    [AvaloniaFact]
    public async Task RestartUsesDraftIdentitiesUntilTheyArePersistedWithDocuments()
    {
        using var first = new TestHost(); var root = Project(first);
        var loader = first.Services.GetRequiredService<WorkspaceSourceLoader>();
        var discovered = await loader.LoadAsync(root, "i18next");
        var checkpoint = new WorkspaceIdentityCheckpoint(root, discovered.IdentityCatalog.Select(e => e with { Id = new(Guid.NewGuid()) }).ToArray());
        var source = await loader.LoadAsync(root, "i18next", identityCheckpoint: checkpoint);
        var engine = first.Services.GetRequiredService<WorkspaceService>(); engine.Open(ProjectSettings.LoadFrom(root)!, source);
        var snapshot = (await engine.SnapshotEntriesAsync())!; var original = snapshot.Units.Single(u => u.Key == "common.title" && u.Language == "en");
        Assert.True(engine.ApplyEntries("editor", new(snapshot.SessionId, snapshot.Revision,
            [new RenameEntry(original.EntryId, original.Namespace, new(["recovered"])), new SetEntryValue(original.EntryId, "en", "Recovered"),
             new SetEntryComment(original.EntryId, "en", "Draft comment")])).Applied);
        first.Services.GetRequiredService<IRecoveryDraftService>().Write(root,
            new(root, DateTime.UtcNow, []) { SchemaVersion = 2, Workspace = engine.CaptureRecovery(source) });
        engine.Close();
        Assert.False(File.Exists(Path.Combine(root, ".toucan/workspace-identities.json")));

        using var restarted = new TestHost(first.Root);
        var lifecycle = restarted.Services.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(root)).Status);
        var restored = restarted.Services.GetRequiredService<WorkspaceService>();
        Assert.Contains((await restored.SnapshotEntriesAsync())!.Units, u => u.EntryId == original.EntryId && u.Key == "common.title");
        Assert.NotNull(lifecycle.PendingRecovery);
        var recovery = lifecycle.ApplyRecovery(); Assert.Empty(recovery.Conflicts); Assert.True(recovery.Applied > 0);
        var unit = (await restored.SnapshotEntriesAsync())!.Units.Single(u => u.EntryId == original.EntryId && u.Language == "en");
        Assert.Equal("Recovered", unit.Value); Assert.Equal("Draft comment", unit.Comment); Assert.Equal(["recovered"], unit.KeyPath.Segments);
        Assert.Contains("Hello", File.ReadAllText(Path.Combine(root, "locales/en/common.json")), StringComparison.Ordinal);
        Assert.True(restored.Undo()); Assert.False(restored.IsDirty); Assert.True(restored.Redo());
        Assert.Equal(ProjectSaveStatus.Success, (await lifecycle.SaveProjectAsync()).Status);
        Assert.True(File.Exists(Path.Combine(root, ".toucan/workspace-identities.json")));
        Assert.Equal(CloseResult.Closed, await lifecycle.CloseProjectAsync());
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(root)).Status);
        Assert.Contains((await restored.SnapshotEntriesAsync())!.Units, u => u.EntryId == original.EntryId && u.Language == "en" && u.Value == "Recovered");
    }

    [AvaloniaFact]
    public async Task MissingExplicitFrameworkCannotFallBackToJson()
    {
        using var host = new TestHost(); var root = Project(host); var settings = ProjectSettings.LoadFrom(root)!; settings.Framework = "missing.framework"; settings.Save();
        var lifecycle = host.Services.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectOpenStatus.ManifestInvalid, (await lifecycle.OpenProjectAsync(root)).Status);
        Assert.False(host.Services.GetRequiredService<WorkspaceService>().IsOpen);
    }
}
