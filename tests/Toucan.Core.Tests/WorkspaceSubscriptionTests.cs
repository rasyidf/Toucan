using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceSubscriptionTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly ProjectSettings _settings = new() { ProjectPath = "/workspace", PrimaryLanguage = "en", Languages = ["en"] };

    public WorkspaceSubscriptionTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _services.GetRequiredService<ITranslationManagementService>().Initialize([new() { Namespace = "hello", Language = "en", Value = "Hello" }]);
        _workspace.Open(_settings);
    }

    [Fact]
    public async Task IndependentSubscribersReceiveOrderedDeltasThatReconstructCommittedState()
    {
        await using var first = (await _workspace.SubscribeAsync())!;
        await using var second = (await _workspace.SubscribeAsync())!;
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", "Changed"));
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().RenameKey("hello", "greeting"));
        _workspace.MarkSaved();
        Assert.True(_workspace.Undo());
        Assert.True(_workspace.Redo());
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().DeleteKey("greeting"));
        var expected = (await _workspace.SnapshotEntriesAsync())!;
        _workspace.Close();
        var a = await Collect(first);
        var b = await Collect(second);
        Assert.Equal(a, b);
        Assert.Equal(7, a.Count);
        var mirror = first.InitialSnapshot.Units.ToDictionary(u => (u.EntryId, u.Language));
        var revision = first.InitialSnapshot.Revision;
        foreach (var change in a)
        {
            Assert.Equal(revision, change.PreviousRevision);
            Assert.True(change.Revision > revision);
            Assert.Equal(first.InitialSnapshot.SessionId, change.SessionId);
            revision = change.Revision;
            foreach (var delta in change.Units)
                if (delta.Unit is null) mirror.Remove((delta.EntryId, delta.Language));
                else mirror[(delta.EntryId, delta.Language)] = delta.Unit;
        }
        Assert.Empty(expected.Units);
        Assert.Empty(mirror);
        Assert.Equal("Hello", Assert.Single(first.InitialSnapshot.Units).Value);
        Assert.Equal(WorkspaceChangeOrigin.Plugin, a[0].Origin);
        Assert.Equal("editor", a[0].OriginId);
        Assert.False(Assert.Single(a[2].Units).Unit!.IsModified);
        Assert.Equal(WorkspaceChangeKind.Closed, a[^1].Kind);
    }

    [Fact]
    public async Task SlowSubscriberRequiresResnapshotWithoutAffectingOtherConsumers()
    {
        await using var slow = (await _workspace.SubscribeAsync(1))!;
        await using var fast = (await _workspace.SubscribeAsync())!;
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", "One"));
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", "Two"));
        var reset = Assert.Single(await Collect(slow));
        Assert.Equal(WorkspaceChangeKind.ResnapshotRequired, reset.Kind);
        Assert.Empty(reset.Units);
        await using var fresh = (await _workspace.SubscribeAsync())!;
        Assert.Equal("Two", Assert.Single(fresh.InitialSnapshot.Units).Value);
        Assert.Equal(reset.Revision, fresh.InitialSnapshot.Revision);
        _workspace.Close();
        Assert.Equal(3, (await Collect(fast)).Count);
    }

    [Fact]
    public async Task ConcurrentSubscribeAndCommitHaveNoSnapshotGap()
    {
        for (var i = 0; i < 20; i++)
        {
            var subscribing = _workspace.SubscribeAsync();
            var editing = _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", $"{i}"));
            await using var subscription = (await subscribing)!;
            await editing;
            _workspace.MarkSaved();
            await using var reader = subscription.Changes.GetAsyncEnumerator();
            var mirror = subscription.InitialSnapshot.Units.ToDictionary(u => (u.EntryId, u.Language));
            var revision = subscription.InitialSnapshot.Revision;
            while (await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
            {
                var change = reader.Current;
                Assert.Equal(revision, change.PreviousRevision);
                revision = change.Revision;
                foreach (var delta in change.Units) mirror[(delta.EntryId, delta.Language)] = delta.Unit!;
                if (change.Kind == WorkspaceChangeKind.Saved) break;
            }
            Assert.Equal($"{i}", Assert.Single(mirror.Values).Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDetachesPendingReader(bool cancelEnumeration)
    {
        using var cancellation = new CancellationTokenSource();
        await using var subscription = (await _workspace.SubscribeAsync(cancellationToken: cancelEnumeration ? default : cancellation.Token))!;
        await using var reader = subscription.Changes.GetAsyncEnumerator(cancelEnumeration ? cancellation.Token : default);
        var pending = reader.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", "Still works"));
    }

    [Fact]
    public async Task SubscriptionDisposalCompletesReaderAndRejectsSecondReader()
    {
        var subscription = (await _workspace.SubscribeAsync())!;
        await using var reader = subscription.Changes.GetAsyncEnumerator();
        var pending = reader.MoveNextAsync().AsTask();
        await using var duplicate = subscription.Changes.GetAsyncEnumerator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate.MoveNextAsync().AsTask());
        await subscription.DisposeAsync();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ClosingIsReadableAndReopenCannotSendEventsToOldSession()
    {
        await using var old = (await _workspace.SubscribeAsync())!;
        _workspace.BeginClosing();
        Assert.True((await _workspace.SnapshotEntriesAsync())!.IsClosing);
        Assert.False((await _workspace.ApplyAsync("editor", new WorkspaceEdit().DeleteKey("hello"))).Applied);
        _workspace.Open(_settings);
        await _workspace.ApplyAsync("editor", new WorkspaceEdit().SetValue("hello", "en", "Reopened"));
        var changes = await Collect(old);
        Assert.Equal([WorkspaceChangeKind.Closing, WorkspaceChangeKind.Closed], changes.Select(c => c.Kind));
        Assert.All(changes, c => Assert.Equal(old.InitialSnapshot.SessionId, c.SessionId));
        Assert.NotEqual(old.InitialSnapshot.SessionId, _workspace.SessionId);
    }

    [Fact]
    public async Task EngineDisposalEndsStreamsAndRejectsNewWork()
    {
        await using var subscription = (await _workspace.SubscribeAsync())!;
        _workspace.Dispose();
        Assert.Equal(WorkspaceChangeKind.Closed, Assert.Single(await Collect(subscription)).Kind);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _workspace.SubscribeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _workspace.ApplyAsync("editor", new WorkspaceEdit()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void InvalidBufferCapacityIsRejected(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = _workspace.SubscribeAsync(capacity); });

    private static async Task<List<WorkspaceChange>> Collect(IWorkspaceSubscription subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changes = new List<WorkspaceChange>();
        await foreach (var change in subscription.Changes.WithCancellation(timeout.Token)) changes.Add(change);
        return changes;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
    }
}
