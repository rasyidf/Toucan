using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public class PluginActivationTests
{
    private sealed class Probe : IPluginActivator
    {
        public Probe(PluginLifetime lifetime) => Lifetime = lifetime;
        public PluginLifetime Lifetime { get; }
        public List<string> Events { get; } = [];
        public List<IPluginActivationContext> Contexts { get; } = [];
        public Func<IPluginActivationContext, CancellationToken, Task>? OnActivate { get; set; }

        public async Task<IAsyncDisposable?> ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Events.Add("activate");
            if (OnActivate is not null) await OnActivate(context, cancellationToken);
            return new Session(this, context);
        }

        private sealed class Session(Probe owner, IPluginActivationContext context) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Events.Add(context.Scope.IsCancellationRequested ? "dispose-after-cancel" : "dispose-before-cancel");
                return ValueTask.CompletedTask;
            }
        }
    }

    private static PluginActivationService Create(params (string plugin, Probe probe)[] probes) =>
        new(probes.Select((p, i) => new RegisteredActivator(p.plugin, $"a{i}", p.probe)));

    [Fact]
    public async Task WorkspaceActivatorsRunPerWorkspaceWithSeparateSessions()
    {
        var probe = new Probe(PluginLifetime.Workspace);
        await using var service = Create(("acme.x", probe));

        await service.OpenWorkspaceAsync("w1");
        await service.OpenWorkspaceAsync("w2");

        Assert.Equal(2, probe.Contexts.Count);
        Assert.Equal(["w1", "w2"], probe.Contexts.Select(c => c.WorkspaceId));
        Assert.NotEqual(probe.Contexts[0].Scope, probe.Contexts[1].Scope);
    }

    [Fact]
    public async Task OnlyMatchingLifetimeRuns()
    {
        var workspace = new Probe(PluginLifetime.Workspace);
        var connection = new Probe(PluginLifetime.Connection);
        await using var service = Create(("a", workspace), ("b", connection));

        await service.OpenWorkspaceAsync("w");
        Assert.Single(workspace.Contexts);
        Assert.Empty(connection.Contexts);

        await service.OpenConnectionAsync("w", "c1");
        Assert.Equal("c1", connection.Contexts.Single().ConnectionId);
    }

    [Fact]
    public async Task ClosingWorkspaceCancelsScopeAndDisposesConnectionsFirst()
    {
        var order = new List<string>();
        var workspace = new Probe(PluginLifetime.Workspace);
        var connection = new Probe(PluginLifetime.Connection);
        await using var service = Create(("a", workspace), ("b", connection));
        await service.OpenWorkspaceAsync("w");
        await service.OpenConnectionAsync("w", "c");
        var workspaceScope = workspace.Contexts.Single().Scope;
        var connectionScope = connection.Contexts.Single().Scope;

        await service.CloseWorkspaceAsync("w");

        Assert.True(workspaceScope.IsCancellationRequested);
        Assert.True(connectionScope.IsCancellationRequested);
        Assert.Equal(["activate", "dispose-after-cancel"], workspace.Events);
        Assert.Equal(["activate", "dispose-after-cancel"], connection.Events);
        Assert.False(service.IsWorkspaceOpen("w"));
        _ = order;
    }

    [Fact]
    public async Task ClosingOneConnectionLeavesTheOthers()
    {
        var connection = new Probe(PluginLifetime.Connection);
        await using var service = Create(("a", connection));
        await service.OpenWorkspaceAsync("w");
        await service.OpenConnectionAsync("w", "c1");
        await service.OpenConnectionAsync("w", "c2");

        await service.CloseConnectionAsync("w", "c1");

        Assert.True(connection.Contexts[0].Scope.IsCancellationRequested);
        Assert.False(connection.Contexts[1].Scope.IsCancellationRequested);
    }

    [Fact]
    public async Task ConnectionNeedsAnOpenWorkspace()
    {
        await using var service = Create(("a", new Probe(PluginLifetime.Connection)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenConnectionAsync("missing", "c"));
    }

    [Fact]
    public async Task FailureIsReportedSeparatelyAndDoesNotStopOtherActivators()
    {
        var bad = new Probe(PluginLifetime.Workspace) { OnActivate = (_, _) => throw new InvalidOperationException("no network") };
        var good = new Probe(PluginLifetime.Workspace);
        await using var service = Create(("bad", bad), ("good", good));
        var raised = new List<ActivationResult>();
        service.ActivationFailed += (_, r) => raised.Add(r.Result);

        var results = await service.OpenWorkspaceAsync("w");

        Assert.Equal([ActivationStatus.Failed, ActivationStatus.Activated], results.Select(r => r.Status));
        Assert.Contains("no network", results[0].Error);
        Assert.Equal("bad", Assert.Single(raised).PluginId);
        Assert.Single(good.Contexts);
    }

    [Fact]
    public async Task ClosingDuringActivationCancelsItAndLeavesNoSession()
    {
        var started = new TaskCompletionSource();
        var probe = new Probe(PluginLifetime.Workspace)
        {
            OnActivate = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };
        await using var service = Create(("a", probe));

        var opening = service.OpenWorkspaceAsync("w");
        await started.Task;
        await service.CloseWorkspaceAsync("w");
        var results = await opening;

        Assert.Equal(ActivationStatus.Cancelled, Assert.Single(results).Status);
        Assert.Equal(["activate"], probe.Events);
    }

    [Fact]
    public async Task ApplicationActivatesOnceAndDisposeEndsIt()
    {
        var probe = new Probe(PluginLifetime.Application);
        var service = Create(("a", probe));

        await service.ActivateApplicationAsync();
        Assert.Empty(await service.ActivateApplicationAsync());
        await service.DisposeAsync();

        Assert.Equal(["activate", "dispose-after-cancel"], probe.Events);
    }

    [Fact]
    public void RegistrationNeedsTheActivationCapability()
    {
        var manifest = new PluginManifest { Id = "acme.x", Capabilities = ["formats"] };
        var context = new PluginContext(manifest, ".", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new ReservedIds());

        Assert.Throws<PluginRegistrationException>(() => context.AddActivator("a", new Probe(PluginLifetime.Workspace)));
    }

    [Fact]
    public void DuplicateActivatorIdsAreRejected()
    {
        var manifest = new PluginManifest { Id = "acme.x", Capabilities = ["activation"] };
        var context = new PluginContext(manifest, ".", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, new ReservedIds());
        context.AddActivator("a", new Probe(PluginLifetime.Workspace));

        Assert.Throws<PluginRegistrationException>(() => context.AddActivator("a", new Probe(PluginLifetime.Workspace)));
    }
}
