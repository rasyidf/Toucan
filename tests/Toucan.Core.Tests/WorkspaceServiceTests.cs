using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceServiceTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly ITranslationManagementService _store;
    private readonly ProjectSettings _settings = new() { ProjectPath = "/workspace", PrimaryLanguage = "en", Languages = ["en", "fr"] };

    public WorkspaceServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _store = _services.GetRequiredService<ITranslationManagementService>();
        _store.Initialize([
            new() { Namespace = "hello", Language = "en", Value = "Hello {name}" },
            new() { Namespace = "hello", Language = "fr", Value = "Bonjour {name}" },
            new() { Namespace = "bye", Language = "en", Value = "Bye" },
            new() { Namespace = "bye", Language = "fr", Value = "Au revoir" }
        ]);
        _workspace.Open(_settings);
    }

    [Fact]
    public async Task CommittedSnapshotsAreImmutableAndNativeChangesUseTheSameRevision()
    {
        var before = (await _workspace.SnapshotAsync())!;
        var projection = _store.Translations.Single(i => i.Namespace == "hello" && i.Language == "fr");
        projection.Value = "Typed";
        Assert.Equal("Bonjour {name}", (await _workspace.SnapshotAsync())!.Find("hello", "fr")!.Value);
        _workspace.CommitProjection(_store.Translations);
        var after = (await _workspace.SnapshotAsync())!;
        Assert.Equal("Bonjour {name}", before.Find("hello", "fr")!.Value);
        Assert.Equal("Typed", after.Find("hello", "fr")!.Value);
        Assert.True(after.Revision > before.Revision);
        Assert.True(_workspace.IsDirty);
        Assert.False((await _workspace.ApplyAsync("plugin", new WorkspaceEdit { BasedOnRevision = before.Revision }.SetValue("bye", "fr", "x"))).Applied);
    }

    [Fact]
    public async Task StructuralEditsAndValuesUndoTogetherWithStableIdentity()
    {
        var before = (await _workspace.SnapshotEntriesAsync())!;
        var identity = before.Units.Single(u => u.Key == "hello" && u.Language == "en").EntryId;
        var result = await _workspace.ApplyAsync("plugin", new WorkspaceEdit().RenameKey("hello", "greetings")
            .SetValue("greetings", "fr", "Salut").DeleteKey("bye"));
        Assert.True(result.Applied);
        var after = (await _workspace.SnapshotEntriesAsync())!;
        Assert.All(after.Units, u => Assert.Equal(identity, u.EntryId));
        Assert.True(_workspace.Undo());
        Assert.False(_workspace.IsDirty);
        Assert.Equal(4, (await _workspace.SnapshotEntriesAsync())!.Units.Count);
        Assert.Equal("Bonjour {name}", (await _workspace.SnapshotAsync())!.Find("hello", "fr")!.Value);
        Assert.True(_workspace.Redo());
        Assert.Equal("Salut", (await _workspace.SnapshotAsync())!.Find("greetings", "fr")!.Value);
        Assert.Null((await _workspace.SnapshotAsync())!.Find("bye", "en"));
    }

    [Fact]
    public async Task DeletedAndApprovedEntriesRemainDirtyEvenWhenTheLegacyStoreCannotSeeThem()
    {
        await _workspace.ApplyAsync("plugin", new WorkspaceEdit().DeleteKey("bye").SetReview("hello", "en", ReviewState.Approved));
        Assert.True(_workspace.IsDirty);
        _workspace.MarkSaved();
        Assert.False(_workspace.IsDirty);
        _workspace.Undo();
        Assert.True(_workspace.IsDirty);
        _workspace.Redo();
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task PartialAndRefusedTransactionsDoNotCreateSpuriousHistory()
    {
        var revision = _workspace.Revision;
        var refused = await _workspace.ApplyAsync("plugin", new WorkspaceEdit().SetValue("hello", "fr", "Changed").DeleteKey("missing"));
        Assert.False(refused.Applied);
        Assert.Equal(revision, _workspace.Revision);
        Assert.False(_workspace.Undo());
        var partial = await _workspace.ApplyAsync("plugin", new WorkspaceEdit { AllowPartial = true }.SetValue("hello", "fr", "Changed").DeleteKey("missing"));
        Assert.True(partial.Applied);
        Assert.Single(partial.Issues);
        Assert.True(_workspace.Undo());
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task ReopenRejectsOldSessionWorkAndCannotReuseHistory()
    {
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        _workspace.Close();
        _workspace.Open(_settings);
        var current = (await _workspace.SnapshotEntriesAsync())!;
        var result = await _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, current.Revision,
            [new SetEntryValue(snapshot.Units[0].EntryId, snapshot.Units[0].Language, "Old session")]));
        Assert.Equal(EditIssueKind.Stale, Assert.Single(result.Issues).Kind);
        Assert.False(_workspace.Undo());
        Assert.NotEqual(snapshot.SessionId, current.SessionId);
    }

    [Fact]
    public async Task IdentifiedEditsDisambiguateLiteralDotsAndSeparateDocuments()
    {
        var first = new EntryId(Guid.NewGuid());
        var second = new EntryId(Guid.NewGuid());
        var document = new DocumentId(Guid.NewGuid());
        var source = new WorkspaceSource("/workspace", "test", FormatIds.Json, [], [
            new(first, "en", TranslationPath.Empty, new(["a.b"]), "Literal", new(document, new(["a.b"]))),
            new(second, "en", TranslationPath.Empty, new(["a", "b"]), "Nested", new(document, new(["a", "b"])))
        ]);
        _workspace.Open(_settings, source);
        var legacy = await _workspace.ApplyAsync("plugin", new WorkspaceEdit().SetValue("a.b", "en", "Wrong"));
        Assert.Equal(EditIssueKind.Ambiguous, Assert.Single(legacy.Issues).Kind);
        Assert.Null((await _workspace.SnapshotAsync())!.Find("a.b", "en"));
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var changed = await _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryValue(first.Value, "en", "Right", "Literal")]));
        Assert.True(changed.Applied);
        var after = (await _workspace.SnapshotEntriesAsync())!;
        Assert.Equal("Right", after.Units.Single(u => u.EntryId == first.Value).Value);
        Assert.Equal("Nested", after.Units.Single(u => u.EntryId == second.Value).Value);
        Assert.Equal("/a.b", after.Units.Single(u => u.EntryId == first.Value).SourceLocation);
    }

    [Fact]
    public async Task ParallelCommandsBasedOnOneRevisionAcceptOnlyOneWriter()
    {
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var unit = snapshot.Units[0];
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => _workspace.ApplyEntriesAsync("plugin",
            new(snapshot.SessionId, snapshot.Revision, [new SetEntryValue(unit.EntryId, unit.Language, $"{i}")])))));
        Assert.Single(results, r => r.Applied);
        Assert.Equal(9, results.Count(r => r.Issues.Any(i => i.Kind == EditIssueKind.Stale)));
    }

    [Fact]
    public async Task StrictApprovalUsesTheFinalTransactionState()
    {
        _settings.RequireValidForApproval = true;
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var unit = snapshot.Units.Single(u => u.Key == "hello" && u.Language == "fr");
        var refused = await _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryValue(unit.EntryId, "fr", "Broken {nom"), new SetEntryReview(unit.EntryId, "fr", ReviewState.Approved)]));
        Assert.False(refused.Applied);
        Assert.False(_workspace.IsDirty);
        var approved = await _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryValue(unit.EntryId, "fr", "Correct {name}"), new SetEntryReview(unit.EntryId, "fr", ReviewState.Approved)]));
        Assert.True(approved.Applied);
    }

    [Fact]
    public async Task NoOpAndCancelledIdentifiedEditsDoNotCreateHistory()
    {
        var snapshot = (await _workspace.SnapshotEntriesAsync())!;
        var unit = snapshot.Units[0];
        var noOp = await _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryValue(unit.EntryId, unit.Language, unit.Value)]));
        Assert.True(noOp.Applied);
        Assert.Equal(0, noOp.ChangedUnits);
        Assert.Equal(snapshot.Revision, noOp.Revision);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _workspace.ApplyEntriesAsync("plugin", new(snapshot.SessionId, snapshot.Revision,
            [new DeleteEntry(unit.EntryId)]), cancelled.Token));
        Assert.False(_workspace.Undo());
    }

    [Fact]
    public async Task ObserverFailureDoesNotUndoAnAcceptedTransactionOrSkipOtherObservers()
    {
        var notifications = 0;
        _workspace.Changed += (_, _) => throw new InvalidOperationException("Failed observer");
        _workspace.Changed += (_, _) => notifications++;
        var result = await _workspace.ApplyAsync("plugin", new WorkspaceEdit().SetValue("bye", "fr", "Changed"));
        Assert.True(result.Applied);
        Assert.Equal(1, notifications);
        Assert.Equal("Changed", (await _workspace.SnapshotAsync())!.Find("bye", "fr")!.Value);
    }

    [Fact]
    public void PersistenceNotificationsWaitUntilTheMutationLeaseEnds()
    {
        var notifications = 0;
        _workspace.Changed += (_, _) => notifications++;
        using (_workspace.ProtectPersistence())
        {
            _workspace.MarkSaved();
            Assert.Equal(0, notifications);
        }
        Assert.Equal(1, notifications);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
    }
}
