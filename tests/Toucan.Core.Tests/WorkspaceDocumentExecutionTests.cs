using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceDocumentExecutionTests : IDisposable
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-document-execution-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly WorkspaceDocumentSavePlanner _planner;
    private readonly WorkspaceSourceLoader _loader;
    private readonly ControlledStore _store = new();
    private readonly WorkspaceDocumentSaveExecutor _executor;

    public WorkspaceDocumentExecutionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _planner = _services.GetRequiredService<WorkspaceDocumentSavePlanner>();
        _loader = _services.GetRequiredService<WorkspaceSourceLoader>();
        _executor = new(_store, _services.GetServices<ILoadStrategy>());
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));
    private async Task<WorkspaceSource> Open()
    {
        var source = await _loader.LoadAsync(_root, "i18next");
        _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en", Languages = ["en", "fr"] }, source);
        return source;
    }
    private Task<EditResult> Edit(string value) => _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", value));
    private WorkspaceDocumentSaveCoordinator Coordinator => new(_workspace, _planner, _executor);
    private string PendingJournal => Path.Combine(SaveTransaction.RecoveryRoot(_root), "pending", "journal.toucan-journal");

    [Fact]
    public async Task NoChangeSaveDoesNotWriteDocumentsOrCreateRecoveryFiles()
    {
        Write("locales/en/common.json", "{ \"title\" : \"Hello\" }\n");
        var source = await Open();
        var timestamp = File.GetLastWriteTimeUtc(Path.Combine(_root, "locales/en/common.json"));
        var result = await Coordinator.SaveAsync(source);
        Assert.True(result.Acknowledged);
        Assert.Empty(result.Receipt.Plan.Targets);
        Assert.Equal(0, _store.Writes);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(_root, "locales/en/common.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".toucan")));
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task SaveWritesOnlyChangedOwnerAndAcknowledgesItsCapturedState()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        Write("locales/en/other.json", "{\"title\":\"Other\"}");
        var source = await Open();
        var untouched = source.Documents.Where(d => d.Language == "fr" || d.Package == "other")
            .ToDictionary(d => d.RelativePath, d => (d.Version, File.GetLastWriteTimeUtc(Path.Combine(_root, d.RelativePath))));
        Assert.True((await Edit("Saved")).Applied);
        var revision = _workspace.Revision;
        var result = await Coordinator.SaveAsync(source);
        Assert.True(result.Acknowledged);
        Assert.Equal(revision, result.Receipt.Plan.CapturedRevision);
        Assert.False(_workspace.IsDirty);
        Assert.Equal(1, _store.Writes);
        Assert.Contains("Saved", Read("locales/en/common.json"), StringComparison.Ordinal);
        foreach (var (path, expected) in untouched)
        {
            Assert.Equal(expected.Version, SaveTransaction.Fingerprint([Path.Combine(_root, path)])[Path.Combine(_root, path)]);
            Assert.Equal(expected.Item2, File.GetLastWriteTimeUtc(Path.Combine(_root, path)));
        }
        Assert.Equal(result.Receipt.Source.Documents.Single(d => d.RelativePath == "locales/en/common.json").Version,
            SaveTransaction.Fingerprint([Path.Combine(_root, "locales/en/common.json")])[Path.Combine(_root, "locales/en/common.json")]);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task EditsDuringSaveRemainDirtyAndTheNextSaveUsesNewSourceVersions()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Captured");
        _store.BeforeWrite = async (_, _, _) => { _store.BeforeWrite = null; await Edit("Later"); };
        var result = await Coordinator.SaveAsync(source);
        Assert.True(result.Acknowledged);
        Assert.Contains("Captured", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Equal("Later", (await _workspace.SnapshotAsync())!.Find("common.title", "en")!.Value);
        Assert.True(_workspace.IsDirty);
        var next = await Coordinator.SaveAsync(result.Receipt.Source);
        Assert.True(next.Acknowledged);
        Assert.False(_workspace.IsDirty);
        Assert.Contains("Later", Read("locales/en/common.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UndoAfterSavingRenameRoutesFromTheSavedAddress()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\",\"other\":\"Other\"}");
        var source = await Open();
        var entry = source.Units.Single(u => u.Value == "Hello");
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        await _workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision,
            [new RenameEntry(entry.EntryId.Value, entry.Namespace, new(["renamed"]))]));
        var saved = await Coordinator.SaveAsync(source);
        Assert.False(_workspace.IsDirty);
        Assert.True(_workspace.Undo());
        var restored = await Coordinator.SaveAsync(saved.Receipt.Source);
        Assert.False(_workspace.IsDirty);
        Assert.Contains("\"title\"", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.DoesNotContain("renamed", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Equal(entry.EntryId, restored.Receipt.Source.Units.Single(u => u.Value == "Hello").EntryId);
    }

    [Fact]
    public async Task UndoAfterSavingDeletionTreatsRestoredEntryAsANewInsertion()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().DeleteKey("common.title"));
        var saved = await Coordinator.SaveAsync(source);
        Assert.Empty(saved.Receipt.Source.Units);
        Assert.True(_workspace.Undo());
        Assert.True(_workspace.IsDirty);
        var restored = await Coordinator.SaveAsync(saved.Receipt.Source);
        Assert.False(_workspace.IsDirty);
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Equal(source.Units.Single().EntryId, restored.Receipt.Source.Units.Single().EntryId);
    }

    [Fact]
    public async Task ExternalChangesBeforeSaveRefuseAllWrites()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Prepared");
        Write("locales/en/common.json", "{\"title\":\"External\"}");
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => Coordinator.SaveAsync(source));
        Assert.Contains("External", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Equal(0, _store.Writes);
        Assert.True(_workspace.IsDirty);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task MidSaveFailureRestoresOnlyDeclaredWrittenTargets()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("common.title", "en", "English").SetValue("common.title", "fr", "French"));
        var untouchedTime = File.GetLastWriteTimeUtc(Path.Combine(_root, "locales/fr/common.json"));
        _store.BeforeWrite = (_, _, _) =>
        {
            if (_store.Writes == 2)
            {
                Write("locales/en/unrelated.json", "{\"external\":\"Keep\"}");
                throw new IOException("Injected second-write failure");
            }
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<IOException>(() => Coordinator.SaveAsync(source));
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Contains("Bonjour", Read("locales/fr/common.json"), StringComparison.Ordinal);
        Assert.Equal(untouchedTime, File.GetLastWriteTimeUtc(Path.Combine(_root, "locales/fr/common.json")));
        Assert.Contains("Keep", Read("locales/en/unrelated.json"), StringComparison.Ordinal);
        Assert.True(_workspace.IsDirty);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task CancellationAfterWriteRollsBackAndPreservesDirtyState()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Cancelled");
        using var cancellation = new CancellationTokenSource();
        _store.AfterWrite = (_, _, _) => { cancellation.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator.SaveAsync(source, cancellationToken: cancellation.Token));
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.True(_workspace.IsDirty);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task ExternalWriteAfterMutationIsPreservedAndRecoveryJournalIsRetained()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Prepared");
        _store.AfterWrite = (_, _, _) => { Write("locales/en/common.json", "{\"title\":\"External\"}"); throw new IOException("Injected failure"); };
        var error = await Assert.ThrowsAsync<WorkspaceSaveRecoveryException>(() => Coordinator.SaveAsync(source));
        Assert.False(error.Rollback!.Complete);
        Assert.Single(error.Rollback.Failed);
        Assert.Contains("External", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.True(File.Exists(PendingJournal));
        var recovery = SaveTransaction.FindInterrupted(_root)!;
        Assert.False(recovery.Rollback().Complete);
        Assert.Contains("External", Read("locales/en/common.json"), StringComparison.Ordinal);
        recovery.Discard();
    }

    [Fact]
    public async Task CloseDuringSaveCancelsTheOldSessionAndCannotAcknowledgeTheNewOne()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Old session");
        _store.BeforeWrite = (_, _, _) => { _workspace.Close(); _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en" }, source); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator.SaveAsync(source));
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task OldSessionReceiptCannotMarkReopenedWorkspaceSaved()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Saved");
        var plan = _planner.Plan(source, _workspace.CaptureDocumentSnapshot()!);
        var receipt = await _executor.ExecuteAsync(plan);
        _workspace.Close();
        _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en" }, receipt.Source);
        await Edit("New session");
        Assert.False(_workspace.AcknowledgeDocumentSave(receipt));
        Assert.True(_workspace.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOnlySavePersistsCommentsAndReviewWithoutWritingDocuments(bool review)
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        var edit = new WorkspaceEdit();
        if (review) edit.SetReview("common.title", "en", ReviewState.Approved);
        else edit.SetComment("common.title", "en", "Unsaved comment");
        await _workspace.ApplyAsync("editor", edit);
        var saved = await Coordinator.SaveAsync(source);
        Assert.True(saved.Acknowledged);
        Assert.False(_workspace.IsDirty);
        Assert.Equal(0, _store.Writes);
        var reopened = await _loader.LoadAsync(_root, "i18next");
        var metadata = Assert.Single(reopened.Units).Metadata!;
        if (review) Assert.True(metadata.Approved);
        else Assert.Equal("Unsaved comment", metadata.Comment);
    }

    [Fact]
    public async Task ActualSavedRawTypesAreReflectedInReceiptAndUndoCanRestoreTheOriginalType()
    {
        Write("locales/en/common.json", "{\"title\":2}");
        var source = await Open();
        await Edit("Two");
        var saved = await Coordinator.SaveAsync(source);
        Assert.Null(saved.Receipt.Source.Units.Single().RawScalar);
        Assert.False(_workspace.IsDirty);
        Assert.True(_workspace.Undo());
        var restored = await Coordinator.SaveAsync(saved.Receipt.Source);
        Assert.Equal("2", restored.Receipt.Source.Units.Single().RawScalar);
        Assert.Contains(":2", Read("locales/en/common.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterruptedVersionTwoJournalRestoresDeclaredCreatesUpdatesAndDeletesOnly()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/en/obsolete.json", "{\"title\":\"Old\"}");
        var source = await Open();
        var obsolete = source.Documents.Single(d => d.Package == "obsolete");
        var current = source.Units.Where(u => u.Binding.DocumentId != obsolete.Id).Select(u => new WorkspaceDocumentUnit(
            u.EntryId, u.Language, u.Namespace, u.KeyPath, "Changed", u.Binding)).ToList();
        var created = new WorkspaceDocumentUnit(new(Guid.NewGuid()), "en", new(["created"]), new(["title"]), "Created", null);
        current.Add(created);
        var plan = _planner.Plan(source, new(_root, _workspace.SessionId, _workspace.Revision, current),
            [new(created.EntryId, "en", "locales/en/created.json")], [obsolete.Id]);
        var receipt = await _executor.ExecuteAsync(plan);
        Assert.Equal(3, receipt.Plan.Targets.Count);
        Assert.False(File.Exists(Path.Combine(_root, obsolete.RelativePath)));
        // The committed generation contains the journal exactly as it stood immediately before commit.
        var recoveryRoot = SaveTransaction.RecoveryRoot(_root);
        Directory.Move(Path.Combine(recoveryRoot, "previous"), Path.Combine(recoveryRoot, "pending"));
        Write("locales/en/unrelated.json", "{\"title\":\"External\"}");
        var interrupted = SaveTransaction.FindInterrupted(_root)!;
        Assert.Equal(4, interrupted.Files.Count);
        Assert.True(interrupted.Rollback().Complete);
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.Contains("Old", Read("locales/en/obsolete.json"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "locales/en/created.json")));
        Assert.Contains("External", Read("locales/en/unrelated.json"), StringComparison.Ordinal);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task ConditionalMutationRefusalPreservesExternalContentWithoutClaimingRollbackOwnership()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Prepared");
        _store.BeforeWrite = (_, _, _) => { Write("locales/en/common.json", "{\"title\":\"External\"}"); return Task.CompletedTask; };
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => Coordinator.SaveAsync(source));
        Assert.Contains("External", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.False(File.Exists(PendingJournal));
        Assert.True(_workspace.IsDirty);
    }

    [Fact]
    public async Task CreateDestinationAppearingAfterPlanningCannotBeOverwritten()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        var added = new WorkspaceDocumentUnit(new(Guid.NewGuid()), "en", new(["created"]), new(["title"]), "Created", null);
        var current = source.Units.Select(u => new WorkspaceDocumentUnit(u.EntryId, u.Language, u.Namespace, u.KeyPath, u.Value, u.Binding)).Append(added);
        var plan = _planner.Plan(source, new(_root, _workspace.SessionId, _workspace.Revision, current),
            [new(added.EntryId, "en", "locales/en/created.json")]);
        _store.BeforeWrite = (_, _, _) => { Write("locales/en/created.json", "{\"title\":\"External\"}"); return Task.CompletedTask; };
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => _executor.ExecuteAsync(plan));
        Assert.Contains("External", Read("locales/en/created.json"), StringComparison.Ordinal);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task UndoAndRedoOfSavedNewEntryKeepRoutingConsistentAcrossAnotherSave()
    {
        Write("locales/en.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().AddKey("new", new Dictionary<string, string> { ["en"] = "New" }));
        var added = await Coordinator.SaveAsync(source);
        Assert.False(_workspace.IsDirty);
        Assert.True(_workspace.Undo());
        var deleted = await Coordinator.SaveAsync(added.Receipt.Source);
        Assert.True(_workspace.Redo());
        Assert.True(_workspace.IsDirty);
        var restored = await Coordinator.SaveAsync(deleted.Receipt.Source);
        Assert.False(_workspace.IsDirty);
        Assert.Contains("New", Read("locales/en.json"), StringComparison.Ordinal);
        Assert.Equal(added.Receipt.Source.Units.Single(u => u.Value == "New").EntryId, restored.Receipt.Source.Units.Single(u => u.Value == "New").EntryId);
    }

    [Fact]
    public async Task ConcurrentSaveCannotReplaceAnActiveRecoveryJournal()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Prepared");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeWrite = async (_, _, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var saving = Coordinator.SaveAsync(source);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var journal = File.ReadAllText(PendingJournal);
            await Assert.ThrowsAsync<IOException>(() => Coordinator.SaveAsync(source));
            var recovery = SaveTransaction.FindInterrupted(_root)!;
            Assert.Throws<IOException>(() => recovery.Rollback());
            Assert.Throws<IOException>(() => recovery.Discard());
            Assert.Equal(journal, File.ReadAllText(PendingJournal));
        }
        finally { release.TrySetResult(); }
        Assert.True((await saving.WaitAsync(TimeSpan.FromSeconds(5))).Acknowledged);
        Assert.False(_workspace.IsDirty);
    }

    [Fact(Skip = "Creating symbolic links on Windows requires additional privileges.", SkipWhen = nameof(IsWindows))]
    public async Task SymbolicLinkTargetsAreRefusedWithoutChangingTheirReferents()
    {
        Write("referent.json", "{\"title\":\"Hello\"}");
        var link = Path.Combine(_root, "locales/en/common.json");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, Path.Combine(_root, "referent.json"));
        var source = await Open();
        await Edit("Prepared");
        await Assert.ThrowsAsync<IOException>(() => Coordinator.SaveAsync(source));
        Assert.Contains("Hello", Read("referent.json"), StringComparison.Ordinal);
        Assert.False(File.Exists(PendingJournal));
    }

    [Fact]
    public async Task SavePublishesAcceptedRevisionAndConcurrentMetadataRemainsDirty()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Captured");
        await using var subscription = (await _workspace.SubscribeAsync())!;
        _store.BeforeWrite = async (_, _, ct) =>
        {
            await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetComment("common.title", "en", "Later comment"), ct);
        };
        var result = await Coordinator.SaveAsync(source);
        Assert.True(result.Acknowledged);
        Assert.True(_workspace.IsDirty);
        Assert.Equal("Later comment", (await _workspace.SnapshotAsync())!.Find("common.title", "en")!.Comment);
        await using var reader = subscription.Changes.GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(WorkspaceChangeKind.Updated, reader.Current.Kind);
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(WorkspaceChangeKind.Saved, reader.Current.Kind);
        Assert.Equal(WorkspaceChangeOrigin.Persistence, reader.Current.Origin);
        Assert.Equal(_workspace.Revision, reader.Current.Revision);
        Assert.True(reader.Current.IsDirty);
    }

    [Fact]
    public void UnversionedLegacyJournalStillRestoresUsingItsOriginalRules()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        SaveTransaction.Begin(_root, [Path.Combine(_root, "locales/en/common.json")]);
        Write("locales/en/common.json", "{\"title\":\"Interrupted\"}");
        var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PendingJournal))!.AsObject();
        journal.Remove("Version");
        journal.Remove("DeclaredTargetsOnly");
        File.WriteAllText(PendingJournal, journal.ToJsonString());
        var interrupted = SaveTransaction.FindInterrupted(_root)!;
        Assert.True(interrupted.Rollback().Complete);
        Assert.Contains("Hello", Read("locales/en/common.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRecoverySchemaIsPreservedAndBlocksFurtherDocumentWrites()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Open();
        await Edit("Saved");
        var saved = await Coordinator.SaveAsync(source);
        var recoveryRoot = SaveTransaction.RecoveryRoot(_root);
        Directory.Move(Path.Combine(recoveryRoot, "previous"), Path.Combine(recoveryRoot, "pending"));
        var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(PendingJournal))!;
        journal["Version"] = 99;
        File.WriteAllText(PendingJournal, journal.ToJsonString());
        var unknown = File.ReadAllText(PendingJournal);
        Assert.Null(SaveTransaction.FindInterrupted(_root));
        await Edit("Next");
        await Assert.ThrowsAsync<IOException>(() => Coordinator.SaveAsync(saved.Receipt.Source));
        Assert.Equal(unknown, File.ReadAllText(PendingJournal));
        Assert.Contains("Saved", Read("locales/en/common.json"), StringComparison.Ordinal);
        Assert.True(_workspace.IsDirty);
    }

    [Fact]
    public async Task SavingPreservesUtf16BomAndNewlines()
    {
        var path = Path.Combine(_root, "locales/en/common.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\r\n  \"title\": \"Hello\"\r\n}\r\n", Encoding.Unicode);
        var source = await Open();
        await Edit("Saved");
        await Coordinator.SaveAsync(source);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()));
        Assert.Equal("{\r\n  \"title\": \"Saved\"\r\n}\r\n", Read("locales/en/common.json"));
    }

    private sealed class ControlledStore : IWorkspaceDocumentStore
    {
        private readonly LocalWorkspaceDocumentStore _local = new();
        public int Writes { get; private set; }
        public Func<string, string, CancellationToken, Task>? BeforeWrite { get; set; }
        public Func<string, string, CancellationToken, Task>? AfterWrite { get; set; }
        public Task<string?> VersionAsync(string root, string path, CancellationToken cancellationToken = default) => _local.VersionAsync(root, path, cancellationToken);
        public async Task WriteAsync(string root, string path, byte[] bytes, string? expectedVersion, CancellationToken cancellationToken = default)
        {
            var document = !path.StartsWith(".toucan/", StringComparison.Ordinal);
            if (document) Writes++;
            if (document && BeforeWrite is not null) await BeforeWrite(root, path, cancellationToken);
            await _local.WriteAsync(root, path, bytes, expectedVersion, cancellationToken);
            if (document && AfterWrite is not null) await AfterWrite(root, path, cancellationToken);
        }
        public Task DeleteAsync(string root, string path, string expectedVersion, CancellationToken cancellationToken = default) => _local.DeleteAsync(root, path, expectedVersion, cancellationToken);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
