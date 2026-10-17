using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceCommandQueueTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly IValidationPipeline _validation = Substitute.For<IValidationPipeline>();
    private readonly ManualResetEventSlim _release = new(false);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorkspaceCommandQueueTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
        var store = _services.GetRequiredService<ITranslationManagementService>();
        store.Initialize([new() { Namespace = "hello", Language = "en", Value = "Hello" }]);
        _workspace = new WorkspaceService(store, _validation, commandCapacity: 1);
        _workspace.Open(new() { ProjectPath = "/workspace", PrimaryLanguage = "en", Languages = ["en"] });
        _validation.RunAll(Arg.Any<ValidationContext>()).Returns(_ =>
        {
            _entered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test validation was not released.");
            return [];
        });
    }

    [Fact]
    public async Task QueueIsOrderedAndFreezesOperationsBeforeWaiting()
    {
        var first = _workspace.ApplyAsync("editor", Edit("First"));
        await _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit = Edit("Second");
        var second = _workspace.ApplyAsync("editor", edit);
        edit.SetValue("hello", "en", "Mutated after submission");
        var third = _workspace.ApplyAsync("editor", Edit("Third"));
        _release.Set();
        var results = await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, r => Assert.True(r.Applied));
        Assert.True(results[0].Revision < results[1].Revision);
        Assert.True(results[1].Revision < results[2].Revision);
        Assert.Equal("Third", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
        Assert.True(_workspace.Undo());
        Assert.Equal("Second", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
    }

    [Fact]
    public async Task QueuedAndBackpressuredCommandsCancelWithoutWaitingForValidation()
    {
        var first = _workspace.ApplyAsync("editor", Edit("First"));
        await _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedCancellation = new CancellationTokenSource();
        using var waitingCancellation = new CancellationTokenSource();
        var queued = _workspace.ApplyAsync("editor", Edit("Queued"), queuedCancellation.Token);
        var waiting = _workspace.ApplyAsync("editor", Edit("Waiting"), waitingCancellation.Token);
        queuedCancellation.Cancel();
        waitingCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        _release.Set();
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Applied);
        Assert.Equal("First", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
    }

    [Fact]
    public async Task CancellationDuringValidationDoesNotCommit()
    {
        var revision = _workspace.Revision;
        using var cancellation = new CancellationTokenSource();
        var applying = _workspace.ApplyAsync("editor", Edit("Cancelled"), cancellation.Token);
        await _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        _release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(revision, _workspace.Revision);
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task CancellationAfterCommitStillReturnsAppliedResult()
    {
        _release.Set();
        using var cancellation = new CancellationTokenSource();
        _workspace.Changed += (_, _) => cancellation.Cancel();
        var result = await _workspace.ApplyAsync("editor", Edit("Accepted"), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.Applied);
        Assert.Equal("Accepted", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
    }

    [Fact]
    public async Task ClosingCancelsQueuedWorkBeforeAnotherSessionCanAcceptIt()
    {
        var first = _workspace.ApplyAsync("editor", Edit("Accepted"));
        await _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = _workspace.ApplyAsync("editor", Edit("Old session"));
        _workspace.Changed += (_, _) => _workspace.BeginClosing();
        _release.Set();
        Assert.True((await first.WaitAsync(TimeSpan.FromSeconds(5))).Applied);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Accepted", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
    }

    [Fact]
    public async Task LegacyObserverCanSubmitAnEditWithoutDeadlockingItsWorker()
    {
        _release.Set();
        EditResult? nested = null;
        _workspace.Changed += (_, _) =>
        {
            if (nested is not null) return;
            nested = new(false, 0, 0, [], []);
            nested = _workspace.ApplyAsync("observer", Edit("Observer")).GetAwaiter().GetResult();
        };
        Assert.True((await _workspace.ApplyAsync("editor", Edit("First")).WaitAsync(TimeSpan.FromSeconds(5))).Applied);
        Assert.True(nested!.Applied);
        Assert.Equal("Observer", (await _workspace.SnapshotAsync())!.Find("hello", "en")!.Value);
    }

    private static WorkspaceEdit Edit(string value) => new WorkspaceEdit().SetValue("hello", "en", value);

    public void Dispose()
    {
        _release.Set();
        _workspace.Dispose();
        _services.Dispose();
        _release.Dispose();
    }
}
