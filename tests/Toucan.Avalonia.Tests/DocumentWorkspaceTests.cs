using Avalonia.Headless.XUnit;
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
    public async Task MissingExplicitFrameworkCannotFallBackToJson()
    {
        using var host = new TestHost(); var root = Project(host); var settings = ProjectSettings.LoadFrom(root)!; settings.Framework = "missing.framework"; settings.Save();
        var lifecycle = host.Services.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectOpenStatus.ManifestInvalid, (await lifecycle.OpenProjectAsync(root)).Status);
        Assert.False(host.Services.GetRequiredService<WorkspaceService>().IsOpen);
    }
}
