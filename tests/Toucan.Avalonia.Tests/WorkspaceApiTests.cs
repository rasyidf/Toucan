using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>A plugin changes the open project through <see cref="IWorkspaceApi"/>; it must behave like the user's own edits.</summary>
public sealed class WorkspaceApiTests : IDisposable
{
    private const string En = """{"hello": "Hi {name}", "bye": "Bye", "app.title": "My App"}""";
    private const string Fr = """{"hello": "Salut {name}", "bye": "Au revoir", "app.title": "Mon Appli"}""";

    private readonly string _root = Directory.CreateTempSubdirectory("toucan-workspace-api-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The real sample plugin is the caller, so the whole path (plugin services → backend → view model) is the one a plugin uses.</summary>
    private TestHost Host(string? root = null)
    {
        var dir = Path.Combine(root ?? _root, "plugins", "sample.tsv");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SamplePlugin")))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        }
        new FilePluginPolicyStore(Path.Combine(root ?? _root, "plugin-policy.json")).Trust("sample.tsv", PluginHasher.Compute(dir));
        return new TestHost(root ?? _root);
    }

    private static IWorkspaceApi Api(TestHost host) => host.Services.GetServices<PluginServicesRegistration>().Single().Services.Workspace;

    private static async Task<(MainWindowViewModel Vm, string Folder)> Open(TestHost host)
    {
        var folder = host.CreateJsonProject("p", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        return (vm, folder);
    }

    private static string Value(MainWindowViewModel vm, string key, string language) =>
        vm.AllTranslation.Single(t => t.Namespace == key && t.Language == language).Value;

    private static Dictionary<(string Ns, string Lang), string> Reload(TestHost host, string folder) =>
        host.Services.GetRequiredService<IProjectService>().LoadProject(folder).Translations
            .GroupBy(t => (t.Namespace, t.Language)).ToDictionary(g => g.Key, g => g.First().Value);

    // ─── reading ───

    [AvaloniaFact]
    public async Task WithNoProjectTheWorkspaceIsClosedAndEditsAreRefused()
    {
        using var host = Host();
        var api = Api(host);

        Assert.False(api.IsOpen);
        Assert.Null(await api.SnapshotAsync());
        var result = await api.ApplyAsync(new WorkspaceEdit().SetValue("hello", "en", "x"));
        Assert.False(result.Applied);
        Assert.Equal(EditIssueKind.NoProject, Assert.Single(result.Issues).Kind);
    }

    [AvaloniaFact]
    public async Task ASnapshotShowsTheProjectIncludingWhatTheUserHasTypedButNotCommitted()
    {
        using var host = Host();
        var (vm, folder) = await Open(host);
        var item = vm.PagingController.Data.Single(g => g.Namespace == "bye").Translations.Single(t => t.Language == "fr");
        item.Value = "Salut les amis"; // typed; the editor commits after a short pause

        var snapshot = (await Api(host).SnapshotAsync())!;

        Assert.True(Api(host).IsOpen);
        Assert.Equal(folder, snapshot.ProjectPath);
        Assert.Equal("en", snapshot.PrimaryLanguage);
        Assert.Equal(["en", "fr"], snapshot.Languages.Order());
        Assert.Equal(["app.title", "bye", "hello"], snapshot.Keys.Order());
        Assert.Equal("Salut les amis", snapshot.Find("bye", "fr")!.Value);
        Assert.True(snapshot.Find("bye", "fr")!.IsModified);
        Assert.False(snapshot.Find("hello", "fr")!.IsModified);
    }

    [AvaloniaFact]
    public async Task ASnapshotDoesNotChangeWhenTheProjectDoes()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        var snapshot = (await Api(host).SnapshotAsync())!;

        await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt"));

        Assert.Equal("Au revoir", snapshot.Find("bye", "fr")!.Value);
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
        Assert.NotEqual(snapshot.Revision, (await Api(host).SnapshotAsync())!.Revision);
    }

    // ─── editing like the user ───

    [AvaloniaFact]
    public async Task AnEditChangesValuesMarksTheProjectDirtyAndUndoesAsOneStep()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        Assert.False(vm.IsDirty);

        var result = await Api(host).ApplyAsync(new WorkspaceEdit { Label = "Pulled 3 translations" }
            .SetValue("bye", "fr", "À bientôt").SetValue("hello", "fr", "Coucou {name}").SetValue("app.title", "fr", "Mon App"));

        Assert.True(result.Applied);
        Assert.Equal(3, result.ChangedUnits);
        Assert.True(vm.IsDirty);
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
        Assert.Contains("Pulled 3 translations", vm.StatusText);
        Assert.True(host.Services.GetRequiredService<ITranslationManagementService>().IsDirty);

        vm.UndoCommand.Execute(null); // one step undoes all three
        Assert.Equal("Au revoir", Value(vm, "bye", "fr"));
        Assert.Equal("Salut {name}", Value(vm, "hello", "fr"));
        Assert.Equal("Mon Appli", Value(vm, "app.title", "fr"));

        vm.RedoCommand.Execute(null);
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
        Assert.Equal("Mon App", Value(vm, "app.title", "fr"));
    }

    [AvaloniaFact]
    public async Task ThePluginsEditsShowUpInTheEditorAndArePlainEditsAfterwards()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        var shown = vm.PagingController.Data.Single(g => g.Namespace == "bye").Translations.Single(t => t.Language == "fr");

        await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt"));

        Assert.Equal("À bientôt", shown.Value); // the open editor card was refreshed
        Assert.Equal(ChangeType.External, vm.AllTranslation.Single(t => t.Namespace == "bye" && t.Language == "fr").ChangeType);
        shown.Value = "Tchao";
        vm.FlushPendingEdits();
        vm.UndoCommand.Execute(null); // the user's own typing is a separate, later step
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task AnEditIsSavedWithTheProjectAndSurvivesReopening()
    {
        using var host = Host();
        var (vm, folder) = await Open(host);

        await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt").SetComment("bye", "en", "Said when leaving"));
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
        Assert.Equal("À bientôt", Reload(host, folder)[("bye", "fr")]);
        using var second = Host(host.Root);
        var reopened = second.CreateViewModel();
        await reopened.OpenProjectAsync(folder);
        Assert.Equal("À bientôt", Value(reopened, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task AFailedSaveKeepsThePluginsEditsUnsavedAndALaterSaveWorks()
    {
        using var host = Host();
        var (vm, folder) = await Open(host);
        await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt"));
        File.Delete(Path.Combine(folder, "fr.json")); // fr.json turns into a folder, so writing French fails
        Directory.CreateDirectory(Path.Combine(folder, "fr.json"));

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.IsDirty);
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
        Assert.Contains(host.Messages.Shown, m => m.StartsWith("Save failed", StringComparison.Ordinal));

        Directory.Delete(Path.Combine(folder, "fr.json"));
        File.WriteAllText(Path.Combine(folder, "fr.json"), Fr);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.Equal("À bientôt", Reload(host, folder)[("bye", "fr")]);
    }

    [AvaloniaFact]
    public async Task APluginsEditsAreOfferedBackAfterACrash()
    {
        using var first = Host();
        var (vm, folder) = await Open(first);
        await Api(first).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt").AddKey("menu.open", new Dictionary<string, string> { ["en"] = "Open" }));
        ((ProjectLifecycleService)first.Services.GetRequiredService<IProjectLifecycleService>()).FlushRecoveryDraft();

        // The app dies here; a new process opens the same project.
        using var second = Host(first.Root);
        var restarted = second.CreateViewModel();
        await restarted.OpenProjectAsync(folder);

        Assert.Single(second.Messages.Choices, c => c.Contains("unsaved change", StringComparison.Ordinal));
        Assert.Equal("À bientôt", Value(restarted, "bye", "fr"));
        Assert.Equal("Open", Value(restarted, "menu.open", "en"));
        Assert.True(restarted.IsDirty);
        Assert.Contains("Au revoir", File.ReadAllText(Path.Combine(folder, "fr.json")), StringComparison.Ordinal); // disk is untouched until saved
    }

    // ─── refusing what is not safe ───

    [AvaloniaFact]
    public async Task ARefusedEditChangesNothing()
    {
        using var host = Host();
        var (vm, _) = await Open(host);

        var result = await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt").SetValue("missing", "fr", "x"));

        Assert.False(result.Applied);
        Assert.Equal(EditIssueKind.UnknownKey, Assert.Single(result.Issues).Kind);
        Assert.Equal("Au revoir", Value(vm, "bye", "fr"));
        Assert.False(vm.IsDirty);
        vm.UndoCommand.Execute(null); // nothing was recorded
        Assert.Equal("Au revoir", Value(vm, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task AnEditPreparedOnStaleDataIsRefusedAndARevisionTracksTheUsersEditsToo()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        var snapshot = (await Api(host).SnapshotAsync())!;

        var item = vm.PagingController.Data.Single(g => g.Namespace == "app.title").Translations.Single(t => t.Language == "en");
        item.Value = "A user edit";
        vm.FlushPendingEdits();

        var result = await Api(host).ApplyAsync(new WorkspaceEdit { BasedOnRevision = snapshot.Revision }.SetValue("bye", "fr", "À bientôt"));

        Assert.Equal(EditIssueKind.Stale, Assert.Single(result.Issues).Kind);
        Assert.Equal("Au revoir", Value(vm, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task AnEditCannotOverwriteATranslationTheUserChangedMeanwhile()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        var item = vm.PagingController.Data.Single(g => g.Namespace == "bye").Translations.Single(t => t.Language == "fr");
        item.Value = "Edited by the user";
        vm.FlushPendingEdits();

        var result = await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "Pulled", expectedValue: "Au revoir"));

        Assert.Equal(EditIssueKind.Conflict, Assert.Single(result.Issues).Kind);
        Assert.Equal("Edited by the user", Value(vm, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task TheStrictApprovalPolicyAppliesToPlugins()
    {
        using var host = Host();
        var (vm, _) = await Open(host);
        await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("hello", "fr", "Salut {nom")); // broken placeholder, which is a validation error
        vm.ProjectSettings!.RequireValidForApproval = true;

        var refused = await Api(host).ApplyAsync(new WorkspaceEdit().SetReview("hello", "fr", ReviewState.Approved));
        Assert.Equal(EditIssueKind.ApprovalRefused, Assert.Single(refused.Issues).Kind);
        Assert.False(vm.AllTranslation.Single(t => t.Namespace == "hello" && t.Language == "fr").IsApproved);

        var partial = await Api(host).ApplyAsync(new WorkspaceEdit { AllowPartial = true }
            .SetReview("hello", "fr", ReviewState.Approved).SetReview("bye", "fr", ReviewState.Approved));
        Assert.True(partial.Applied);
        Assert.Equal(1, partial.ChangedUnits);
        Assert.Single(partial.Issues);
        Assert.True(vm.AllTranslation.Single(t => t.Namespace == "bye" && t.Language == "fr").IsApproved);
        Assert.Equal(ReviewState.Approved, (await Api(host).SnapshotAsync())!.Find("bye", "fr")!.Review);
        Assert.True(vm.IsDirty);
    }

    // ─── keys ───

    [AvaloniaFact]
    public async Task KeysCanBeAddedRenamedAndDeletedAndAreSavedThatWay()
    {
        using var host = Host();
        var (vm, folder) = await Open(host);

        var result = await Api(host).ApplyAsync(new WorkspaceEdit()
            .AddKey("menu.open", new Dictionary<string, string> { ["en"] = "Open", ["fr"] = "Ouvrir" })
            .RenameKey("menu.open", "menu.openFile")
            .RenameKey("app", "ui")
            .DeleteKey("bye"));

        Assert.True(result.Applied, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.Equal(["hello", "menu.openFile", "ui.title"], vm.AllTranslation.Select(t => t.Namespace).Distinct().Order());
        Assert.True(vm.IsDirty);

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = Reload(host, folder);
        Assert.Equal("Ouvrir", saved[("menu.openFile", "fr")]);
        Assert.Equal("My App", saved[("ui.title", "en")]);
        Assert.DoesNotContain(("bye", "en"), saved.Keys);
    }

    // ─── the sample command ───

    [AvaloniaFact]
    public async Task TheSamplesCopySourceCommandFillsEmptyTranslationsInOneUndoStep()
    {
        using var host = Host();
        var folder = host.CreateJsonProject("p", ("en", """{"a": "Alpha", "b": "Beta", "c": "Gamma"}"""), ("fr", """{"a": "", "b": "Bêta"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        var run = await host.Services.GetRequiredService<Toucan.Core.Commands.ICommandRegistry>().ExecuteAsync("sample.tsv.copy-source");

        Assert.Equal(Toucan.Core.Commands.CommandRunStatus.Completed, run.Status);
        Assert.Equal("Alpha", Value(vm, "a", "fr"));
        Assert.Equal("Gamma", Value(vm, "c", "fr")); // a translation that was missing altogether
        Assert.Equal("Bêta", Value(vm, "b", "fr"));  // already filled: untouched
        Assert.Contains(host.Services.GetRequiredService<INotificationCenter>().History, n => n.Content.Title == "Source text copied");

        vm.UndoCommand.Execute(null);
        Assert.Equal(string.Empty, Value(vm, "a", "fr"));
        Assert.Equal(string.Empty, Value(vm, "c", "fr"));
    }

    // ─── threading and notification ───

    [AvaloniaFact]
    public async Task AnEditFromABackgroundThreadIsAppliedOnTheUiThread()
    {
        using var host = Host();
        var (vm, _) = await Open(host);

        var result = await Task.Run(() => Api(host).ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt")));

        Assert.True(result.Applied);
        Assert.Equal("À bientôt", Value(vm, "bye", "fr"));
    }

    [AvaloniaFact]
    public async Task PluginsAreToldWhenTheProjectChanges()
    {
        using var host = Host();
        var api = Api(host);
        var changes = 0;
        api.Changed += (_, _) => changes++;
        var (vm, _) = await Open(host);
        var afterOpen = changes;
        Assert.True(afterOpen > 0);

        await api.ApplyAsync(new WorkspaceEdit().SetValue("bye", "fr", "À bientôt"));
        Assert.True(changes > afterOpen);

        var typing = changes;
        var item = vm.PagingController.Data.Single(g => g.Namespace == "app.title").Translations.Single(t => t.Language == "en");
        item.Value = "User typing";
        vm.FlushPendingEdits();
        Assert.True(changes > typing);
    }

    // ─── what the edit left behind ───

    [AvaloniaFact]
    public async Task ValidationFindingsComeBackAndShowInTheIssuesPanelWithoutBlockingTheEdit()
    {
        using var host = Host();
        var (vm, _) = await Open(host);

        var result = await Api(host).ApplyAsync(new WorkspaceEdit().SetValue("hello", "fr", "Salut {nom"));

        Assert.True(result.Applied);
        Assert.Contains(result.Findings, f => f.Key == "hello" && f.Language == "fr");
        Assert.True(vm.HasValidationIssues);
        Assert.Equal("Salut {nom", Value(vm, "hello", "fr")); // findings never refuse or revert
        Assert.True(vm.IsDirty);
    }
}
