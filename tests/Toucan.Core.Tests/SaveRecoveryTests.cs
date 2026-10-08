using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Tests.Formats;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>Failure scenarios for saving: nothing is lost, nothing unsaved is reported as saved.</summary>
public sealed class SaveRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-recovery-").FullName;
    private readonly string _project;
    private readonly string _drafts;
    private readonly List<ValidationResult> _findings = [];

    public SaveRecoveryTests()
    {
        _project = Path.Combine(_root, "project");
        _drafts = Path.Combine(_root, "drafts");
        Directory.CreateDirectory(_project);
        new ProjectService(new FileService(NullLogger<FileService>.Instance), FormatTestHost.SaveStrategies, FormatTestHost.Factory,
                new ProjectModeResolver(), NullLogger<ProjectService>.Instance)
            .CreateProject(_project, ["en", "de"], FormatIds.Json);
        File.WriteAllText(Path.Combine(_project, "en.json"), "{\n  \"app\": { \"title\": \"Hello\", \"bye\": \"Bye\" }\n}\n");
        File.WriteAllText(Path.Combine(_project, "de.json"), "{\n  \"app\": { \"title\": \"Hallo\", \"bye\": \"Tschüss\" }\n}\n");
    }

    public void Dispose()
    {
        // Undo a read-only folder from the permission test so cleanup can proceed.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_project, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class Session
    {
        public required ProjectLifecycleService Lifecycle { get; init; }
        public required TranslationManagementService Store { get; init; }
        public required RecoveryDraftService Drafts { get; init; }
        public required IFileWatcherService Watcher { get; init; }
    }

    private async Task<Session> OpenAsync(UnsavedChangesChoice onClose = UnsavedChangesChoice.Save, bool mergeExternalChanges = false, Toucan.Core.Plugins.IPluginActivationService? activation = null)
    {
        var store = new TranslationManagementService(Substitute.For<IUndoRedoService>());
        var projects = new ProjectService(new FileService(NullLogger<FileService>.Instance), FormatTestHost.SaveStrategies, FormatTestHost.Factory,
            new ProjectModeResolver(), NullLogger<ProjectService>.Instance);
        var watcher = Substitute.For<IFileWatcherService>();
        var validation = Substitute.For<IValidationPipeline>();
        validation.RunAll(Arg.Any<ValidationContext>()).Returns(_ => _findings.ToList());
        var unsaved = Substitute.For<IUnsavedChangesHandler>();
        unsaved.PromptAsync().Returns(_ => Task.FromResult(onClose));
        var drafts = new RecoveryDraftService(_drafts);
        var external = Substitute.For<IExternalChangeHandler>();
        external.PromptAsync().Returns(Task.FromResult(ExternalChangeChoice.Merge));
        var lifecycle = new ProjectLifecycleService(projects, store, watcher, validation, Substitute.For<IAutoSaveService>(),
            mergeExternalChanges ? new DiffMergeEngine() : Substitute.For<IDiffMergeEngine>(), Substitute.For<IAuditService>(), Substitute.For<IRecentProjectService>(),
            new CommentPersistenceService(NullLogger<CommentPersistenceService>.Instance, FormatTestHost.Factory),
            new LanguageManagementService(store, projects, watcher, NullLogger<LanguageManagementService>.Instance),
            unsaved, external, NullLogger<ProjectLifecycleService>.Instance, drafts, activation);
        var session = new Session { Lifecycle = lifecycle, Store = store, Drafts = drafts, Watcher = watcher };
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(_project)).Status);
        return session;
    }

    [Fact]
    public async Task PluginsActivateWhenTheProjectOpensAndEndWhenItCloses()
    {
        var activation = Substitute.For<Toucan.Core.Plugins.IPluginActivationService>();
        activation.OpenWorkspaceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Toucan.Core.Plugins.ActivationResult>>([]));
        var s = await OpenAsync(UnsavedChangesChoice.Discard, activation: activation);

        await activation.Received(1).OpenWorkspaceAsync(s.Lifecycle.CurrentProject!.ProjectPath, Arg.Any<CancellationToken>());
        await activation.DidNotReceive().CloseWorkspaceAsync(Arg.Any<string>());

        var path = s.Lifecycle.CurrentProject.ProjectPath;
        await s.Lifecycle.CloseProjectAsync();

        await activation.Received(1).CloseWorkspaceAsync(path);
    }

    private static TranslationItem Item(Session s, string language, string key) =>
        s.Store.Translations.Single(t => t.Language == language && t.Namespace == key);

    private string Read(string file) => File.ReadAllText(Path.Combine(_project, file));

    [Fact]
    public async Task ValidationFindings_DoNotBlockSavingADraft()
    {
        var s = await OpenAsync();
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "Draft {broken");
        _findings.Add(new ValidationResult("placeholder", ValidationSeverity.Error, "broken", "app.title", "en"));

        var result = await s.Lifecycle.SaveProjectAsync();

        Assert.Equal(ProjectSaveStatus.Success, result.Status);
        Assert.Single(result.Findings!);
        Assert.Contains("Draft {broken", Read("en.json"));
        Assert.False(s.Store.IsDirty);
    }

    [Fact]
    public async Task StrictPolicy_RefusesToSaveWhileErrorsExist()
    {
        var s = await OpenAsync();
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "Changed");
        _findings.Add(new ValidationResult("r", ValidationSeverity.Error, "bad"));

        var result = await s.Lifecycle.SaveProjectAsync(new SaveOptions(EnforceValidation: true));

        Assert.Equal(ProjectSaveStatus.ValidationErrors, result.Status);
        Assert.Contains("Hello", Read("en.json"));
        Assert.True(s.Store.IsDirty);
    }

    [Fact]
    public async Task FailureInTheMiddleOfAMultiFileSave_RestoresEveryFileAndKeepsWorkUnsaved()
    {
        var s = await OpenAsync();
        var before = Read("en.json");
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "New English");
        s.Store.NotifyValueChanged(Item(s, "de", "app.title"), "Neues Deutsch");
        // de.json becomes a directory: the English file is written first, then the German write fails.
        File.Delete(Path.Combine(_project, "de.json"));
        Directory.CreateDirectory(Path.Combine(_project, "de.json"));

        var result = await s.Lifecycle.SaveProjectAsync(new SaveOptions(OverwriteExternalChanges: true));

        Assert.Equal(ProjectSaveStatus.FileSystemError, result.Status);
        Assert.Contains("restored", result.ErrorMessage);
        Assert.Equal(before, Read("en.json"));
        Assert.True(s.Store.IsDirty);
        Assert.Equal("New English", Item(s, "en", "app.title").Value);
        Assert.Empty(Directory.EnumerateFiles(_project, ".*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PermissionError_ReportsFailureAndKeepsWorkUnsaved()
    {
        if (OperatingSystem.IsWindows()) return;
        var s = await OpenAsync();
        var before = Read("en.json");
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "New");
        File.SetUnixFileMode(_project, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        if (Environment.UserName == "root") return;

        var result = await s.Lifecycle.SaveProjectAsync();

        Assert.Equal(ProjectSaveStatus.FileSystemError, result.Status);
        Assert.True(s.Store.IsDirty);
        Assert.Equal(before, Read("en.json"));
    }

    [Fact]
    public async Task ExternalEditBeforeSave_IsNotOverwrittenUnlessConfirmed()
    {
        var s = await OpenAsync();
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "Mine");
        File.WriteAllText(Path.Combine(_project, "en.json"), "{\n  \"app\": { \"title\": \"Theirs\", \"bye\": \"Bye\" }\n}\n");

        var blocked = await s.Lifecycle.SaveProjectAsync();

        Assert.Equal(ProjectSaveStatus.ExternalChanges, blocked.Status);
        Assert.Equal("en.json", Path.GetFileName(Assert.Single(blocked.ExternalFiles!)));
        Assert.Contains("Theirs", Read("en.json"));
        Assert.True(s.Store.IsDirty);

        var forced = await s.Lifecycle.SaveProjectAsync(new SaveOptions(OverwriteExternalChanges: true));

        Assert.Equal(ProjectSaveStatus.Success, forced.Status);
        Assert.Contains("Mine", Read("en.json"));
        Assert.Equal(ProjectSaveStatus.Success, (await s.Lifecycle.SaveProjectAsync()).Status);
    }

    [Fact]
    public async Task SuccessfulSave_KeepsThePreviousVersionOfEachFile()
    {
        var s = await OpenAsync();
        var before = Read("en.json");
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "New");

        Assert.Equal(ProjectSaveStatus.Success, (await s.Lifecycle.SaveProjectAsync()).Status);

        var previous = Path.Combine(SaveTransaction.RecoveryRoot(_project), "previous");
        Assert.Contains(Directory.GetFiles(previous, "*.bak"), f => File.ReadAllText(f) == before);
        Assert.False(Directory.Exists(Path.Combine(SaveTransaction.RecoveryRoot(_project), "pending")));
    }

    [Fact]
    public async Task InterruptedSave_IsOfferedForRestoreOnNextOpen()
    {
        var original = Read("de.json");
        // A save that died after starting: journal written, one file half-way changed, no commit.
        SaveTransaction.Begin(_project, [Path.Combine(_project, "de.json"), Path.Combine(_project, "fr.json")]);
        File.WriteAllText(Path.Combine(_project, "de.json"), "{ \"app\": ");
        File.WriteAllText(Path.Combine(_project, "fr.json"), "{}");

        var s = await OpenAsync();
        Assert.True(s.Lifecycle.HasInterruptedSave);

        var result = await s.Lifecycle.RestoreInterruptedSaveAsync();

        Assert.True(result.Complete);
        Assert.Equal(original, Read("de.json"));
        Assert.False(File.Exists(Path.Combine(_project, "fr.json")));
        Assert.False(s.Lifecycle.HasInterruptedSave);
    }

    [Fact]
    public async Task UnsavedEdits_SurviveACrashAndComeBackOnReopen()
    {
        var first = await OpenAsync();
        first.Store.NotifyValueChanged(Item(first, "en", "app.title"), "Edited before crash");
        first.Store.AddItems([new TranslationItem { Language = "en", Namespace = "app.fresh", Value = "Brand new" }]);
        first.Lifecycle.FlushRecoveryDraft();
        // "Crash": the first session is simply abandoned without saving or closing.

        var second = await OpenAsync();
        Assert.NotNull(second.Lifecycle.PendingRecovery);
        Assert.False(second.Store.IsDirty);

        var applied = second.Lifecycle.ApplyRecovery();

        Assert.Equal(2, applied.Applied);
        Assert.Empty(applied.Conflicts);
        Assert.Equal("Edited before crash", Item(second, "en", "app.title").Value);
        Assert.Equal("Brand new", Item(second, "en", "app.fresh").Value);
        Assert.True(second.Store.IsDirty);
        Assert.Contains("Hello", Read("en.json"));
    }

    [Fact]
    public async Task Recovery_NeverReplacesNewerDiskContent()
    {
        var first = await OpenAsync();
        first.Store.NotifyValueChanged(Item(first, "en", "app.title"), "My edit");
        first.Store.NotifyValueChanged(Item(first, "en", "app.bye"), "My bye");
        first.Lifecycle.FlushRecoveryDraft();
        // The file is changed elsewhere (one of the two edited keys) before the project is reopened.
        File.WriteAllText(Path.Combine(_project, "en.json"), "{\n  \"app\": { \"title\": \"Changed on disk\", \"bye\": \"Bye\" }\n}\n");

        var second = await OpenAsync();
        var applied = second.Lifecycle.ApplyRecovery();

        Assert.Equal(1, applied.Applied);
        Assert.Equal("app.title", Assert.Single(applied.Conflicts).Namespace);
        Assert.Equal("Changed on disk", Item(second, "en", "app.title").Value);
        Assert.Equal("My bye", Item(second, "en", "app.bye").Value);
    }

    [Fact]
    public async Task DraftIsRemovedAfterASuccessfulSaveAndWhenEditsAreDiscarded()
    {
        var saved = await OpenAsync();
        saved.Store.NotifyValueChanged(Item(saved, "en", "app.title"), "Saved");
        saved.Lifecycle.FlushRecoveryDraft();
        Assert.NotNull(saved.Drafts.TryRead(_project));
        await saved.Lifecycle.SaveProjectAsync();
        Assert.Null(saved.Drafts.TryRead(_project));

        var discarded = await OpenAsync(UnsavedChangesChoice.Discard);
        discarded.Store.NotifyValueChanged(Item(discarded, "en", "app.title"), "Thrown away");
        discarded.Lifecycle.FlushRecoveryDraft();
        Assert.NotNull(discarded.Drafts.TryRead(_project));
        await discarded.Lifecycle.CloseProjectAsync();
        Assert.Null(discarded.Drafts.TryRead(_project));
    }

    [Fact]
    public async Task UndecidedRecoveryDraft_IsNotOverwrittenByTheTimer()
    {
        var first = await OpenAsync();
        first.Store.NotifyValueChanged(Item(first, "en", "app.title"), "Precious");
        first.Lifecycle.FlushRecoveryDraft();

        var second = await OpenAsync();
        second.Lifecycle.FlushRecoveryDraft(); // nothing dirty in this session: must not delete the old draft

        Assert.Equal("Precious", second.Drafts.TryRead(_project)!.Entries.Single().Value);
    }

    [Fact]
    public async Task Recovery_RestoresDeletedKeysAndApprovals()
    {
        var first = await OpenAsync();
        first.Store.RemoveItems(t => t.Namespace == "app.bye");
        Item(first, "de", "app.title").IsApproved = true;
        first.Lifecycle.FlushRecoveryDraft();

        var second = await OpenAsync();
        Assert.Equal(3, second.Lifecycle.PendingRecovery!.ChangeCount); // two deletions (en, de) and one approval
        var applied = second.Lifecycle.ApplyRecovery();

        Assert.Equal(3, applied.Applied);
        Assert.DoesNotContain(second.Store.Translations, t => t.Namespace == "app.bye");
        Assert.True(Item(second, "de", "app.title").IsApproved);
        Assert.Equal(ProjectSaveStatus.Success, (await second.Lifecycle.SaveProjectAsync()).Status);
        Assert.DoesNotContain("bye", Read("en.json"));
    }

    [Fact]
    public async Task Recovery_DoesNotDeleteAKeyThatChangedOnDisk()
    {
        var first = await OpenAsync();
        first.Store.RemoveItems(t => t.Language == "en" && t.Namespace == "app.bye");
        first.Lifecycle.FlushRecoveryDraft();
        File.WriteAllText(Path.Combine(_project, "en.json"), "{\n  \"app\": { \"title\": \"Hello\", \"bye\": \"Changed elsewhere\" }\n}\n");

        var second = await OpenAsync();
        var applied = second.Lifecycle.ApplyRecovery();

        Assert.Equal(0, applied.Applied);
        Assert.Equal("app.bye", Assert.Single(applied.Conflicts).Namespace);
        Assert.Equal("Changed elsewhere", Item(second, "en", "app.bye").Value);
    }

    [Fact]
    public async Task RollbackAlsoRemovesFilesTheSaveCreatedThatNobodyListed()
    {
        var transaction = SaveTransaction.Begin(_project, [Path.Combine(_project, "en.json")]);
        File.WriteAllText(Path.Combine(_project, "en.json"), "changed");
        File.WriteAllText(Path.Combine(_project, "it.json"), "{}"); // created by the save, never listed
        File.WriteAllText(Path.Combine(_project, "notes.txt"), "not a project file");

        var result = transaction.Rollback();

        Assert.True(result.Complete);
        Assert.False(File.Exists(Path.Combine(_project, "it.json")));
        Assert.True(File.Exists(Path.Combine(_project, "notes.txt")));
        Assert.Contains("Hello", Read("en.json"));
    }

    [Fact]
    public async Task ExternalMerge_KeepsLocalEditsUnsaved_AndTakesDiskChangesAsSaved()
    {
        var s = await OpenAsync(mergeExternalChanges: true);
        s.Store.NotifyValueChanged(Item(s, "en", "app.title"), "Mine");
        File.WriteAllText(Path.Combine(_project, "en.json"), "{\n  \"app\": { \"title\": \"Hello\", \"bye\": \"Bye from disk\" }\n}\n");

        s.Watcher.FilesChanged += Raise.Event();
        for (var i = 0; i < 50 && Item(s, "en", "app.bye").Value != "Bye from disk"; i++) await Task.Delay(50);

        Assert.Equal("Bye from disk", Item(s, "en", "app.bye").Value);
        Assert.Equal("Mine", Item(s, "en", "app.title").Value);
        Assert.Equal(["app.title"], s.Store.GetDirtyItems().Where(t => t.Language == "en").Select(t => t.Namespace));

        // The merge acknowledged the disk change, so saving is allowed and writes both.
        Assert.Equal(ProjectSaveStatus.Success, (await s.Lifecycle.SaveProjectAsync()).Status);
        var saved = Read("en.json");
        Assert.Contains("Mine", saved);
        Assert.Contains("Bye from disk", saved);
        Assert.False(s.Store.IsDirty);
    }
}
