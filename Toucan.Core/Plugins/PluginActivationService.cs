using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>An activator a plugin registered, tied to the plugin that owns it.</summary>
public sealed record RegisteredActivator(string PluginId, string Id, IPluginActivator Activator);

public enum ActivationStatus
{
    Activated,

    /// <summary>The activator threw. Other activators and the application are unaffected.</summary>
    Failed,

    /// <summary>The caller's token or the scope ended before activation finished; nothing is left running.</summary>
    Cancelled,
}

public sealed record ActivationResult(
    string PluginId,
    string ActivatorId,
    PluginLifetime Lifetime,
    string? WorkspaceId,
    string? ConnectionId,
    ActivationStatus Status,
    string? Error = null);

public sealed class ActivationFailedEventArgs(ActivationResult result) : EventArgs
{
    public ActivationResult Result { get; } = result;
}

public sealed class WorkspaceEventArgs(string workspaceId) : EventArgs
{
    public string WorkspaceId { get; } = workspaceId;
}

/// <summary>
/// Runs plugin activators for the application, each open project and each connection. Registration happens at
/// startup without side effects; this is where a plugin first touches the network or disk. A failing activator
/// never affects another, and closing a scope cancels its token and disposes its sessions.
/// </summary>
public interface IPluginActivationService
{
    /// <summary>Raised for every activation that failed, in addition to the returned results.</summary>
    event EventHandler<ActivationFailedEventArgs>? ActivationFailed;

    /// <summary>Raised after a workspace scope starts (before its activators run).</summary>
    event EventHandler<WorkspaceEventArgs>? WorkspaceOpened;

    /// <summary>Raised after a workspace scope has ended and its sessions are disposed.</summary>
    event EventHandler<WorkspaceEventArgs>? WorkspaceClosed;

    Task<IReadOnlyList<ActivationResult>> ActivateApplicationAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts the workspace scope and runs the workspace activators. Re-opening an open workspace is a no-op.</summary>
    Task<IReadOnlyList<ActivationResult>> OpenWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Runs the connection activators. The workspace must be open.</summary>
    Task<IReadOnlyList<ActivationResult>> OpenConnectionAsync(string workspaceId, string connectionId, CancellationToken cancellationToken = default);

    Task CloseConnectionAsync(string workspaceId, string connectionId);

    /// <summary>Closes the workspace's connections, then the workspace itself.</summary>
    Task CloseWorkspaceAsync(string workspaceId);

    bool IsWorkspaceOpen(string workspaceId);

    /// <summary>The projects that are open now, so a service created later can catch up with what it missed.</summary>
    IReadOnlyList<string> OpenWorkspaceIds { get; }
}

public sealed class PluginActivationService : IPluginActivationService, IAsyncDisposable, IDisposable
{
    private readonly IReadOnlyList<RegisteredActivator> _activators;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _applicationScope = new();
    private readonly List<IAsyncDisposable> _applicationSessions = [];
    private readonly Dictionary<string, WorkspaceScope> _workspaces = new(StringComparer.Ordinal);
    private bool _applicationActivated;

    public PluginActivationService(IEnumerable<RegisteredActivator> activators, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(activators);
        _activators = activators.ToList();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<PluginActivationService>();
    }

    public event EventHandler<ActivationFailedEventArgs>? ActivationFailed;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceOpened;
    public event EventHandler<WorkspaceEventArgs>? WorkspaceClosed;

    public bool IsWorkspaceOpen(string workspaceId) => _workspaces.ContainsKey(workspaceId);

    public IReadOnlyList<string> OpenWorkspaceIds => [.. _workspaces.Keys];

    public async Task<IReadOnlyList<ActivationResult>> ActivateApplicationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_applicationActivated) return [];
            _applicationActivated = true;
            return await RunAsync(PluginLifetime.Application, null, null, _applicationSessions, _applicationScope.Token, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActivationResult>> OpenWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_workspaces.ContainsKey(workspaceId)) return [];
            var scope = new WorkspaceScope(CancellationTokenSource.CreateLinkedTokenSource(_applicationScope.Token));
            _workspaces[workspaceId] = scope;
            WorkspaceOpened?.Invoke(this, new WorkspaceEventArgs(workspaceId));
            return await RunAsync(PluginLifetime.Workspace, workspaceId, null, scope.Sessions, scope.Cts.Token, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActivationResult>> OpenConnectionAsync(string workspaceId, string connectionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_workspaces.TryGetValue(workspaceId, out var workspace))
                throw new InvalidOperationException($"Workspace '{workspaceId}' is not open.");
            if (workspace.Connections.ContainsKey(connectionId)) return [];

            var scope = new ConnectionScope(CancellationTokenSource.CreateLinkedTokenSource(workspace.Cts.Token));
            workspace.Connections[connectionId] = scope;
            return await RunAsync(PluginLifetime.Connection, workspaceId, connectionId, scope.Sessions, scope.Cts.Token, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseConnectionAsync(string workspaceId, string connectionId)
    {
        // Cancel before waiting for the gate so an activation still in flight ends promptly.
        if (_workspaces.TryGetValue(workspaceId, out var peek) && peek.Connections.TryGetValue(connectionId, out var pending))
            await CancelAsync(pending.Cts).ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_workspaces.TryGetValue(workspaceId, out var workspace) || !workspace.Connections.Remove(connectionId, out var scope)) return;
            await TearDownAsync(scope.Cts, scope.Sessions, $"connection {connectionId}").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseWorkspaceAsync(string workspaceId)
    {
        if (_workspaces.TryGetValue(workspaceId, out var pending))
            await CancelAsync(pending.Cts).ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_workspaces.Remove(workspaceId, out var workspace)) return;
            foreach (var (id, connection) in workspace.Connections)
                await TearDownAsync(connection.Cts, connection.Sessions, $"connection {id}").ConfigureAwait(false);
            await TearDownAsync(workspace.Cts, workspace.Sessions, $"workspace {workspaceId}").ConfigureAwait(false);
            WorkspaceClosed?.Invoke(this, new WorkspaceEventArgs(workspaceId));
        }
        finally
        {
            _gate.Release();
        }
    }

    // Containers built without async disposal call this; Task.Run keeps a UI sync context from deadlocking it.
    public void Dispose() => Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _workspaces.Keys.ToList())
            await CloseWorkspaceAsync(id).ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TearDownAsync(_applicationScope, _applicationSessions, "application").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        _gate.Dispose();
    }

    // Plugin code is untrusted: whatever an activator throws fails that activation only.
#pragma warning disable CA1031
    private async Task<IReadOnlyList<ActivationResult>> RunAsync(PluginLifetime lifetime, string? workspaceId, string? connectionId,
        List<IAsyncDisposable> sessions, CancellationToken scope, CancellationToken caller)
    {
        var results = new List<ActivationResult>();
        foreach (var registered in _activators.Where(a => a.Activator.Lifetime == lifetime))
        {
            ActivationResult Result(ActivationStatus status, string? error = null) =>
                new(registered.PluginId, registered.Id, lifetime, workspaceId, connectionId, status, error);

            ActivationResult result;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope, caller);
                var context = new ActivationContext(registered.PluginId, lifetime, workspaceId, connectionId,
                    _loggerFactory.CreateLogger($"Plugin.{registered.PluginId}"), scope);
                var session = await registered.Activator.ActivateAsync(context, linked.Token).ConfigureAwait(false);
                if (session is not null)
                {
                    if (linked.IsCancellationRequested)
                        await SafeDisposeAsync(session, registered).ConfigureAwait(false);
                    else
                        sessions.Add(session);
                }
                result = Result(linked.IsCancellationRequested ? ActivationStatus.Cancelled : ActivationStatus.Activated);
            }
            catch (OperationCanceledException)
            {
                result = Result(ActivationStatus.Cancelled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Id} activator {Activator} failed to activate.", registered.PluginId, registered.Id);
                result = Result(ActivationStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
                ActivationFailed?.Invoke(this, new ActivationFailedEventArgs(result));
            }
            results.Add(result);
        }
        return results;
    }

    private async Task TearDownAsync(CancellationTokenSource cts, List<IAsyncDisposable> sessions, string what)
    {
        await CancelAsync(cts).ConfigureAwait(false);
        // Newest first, like nested using blocks.
        for (var i = sessions.Count - 1; i >= 0; i--)
        {
            try
            {
                await sessions[i].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A plugin session failed to dispose when closing {Scope}.", what);
            }
        }
        sessions.Clear();
        cts.Dispose();
    }

    private static async Task CancelAsync(CancellationTokenSource cts)
    {
        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }
        catch (AggregateException)
        {
            // A cancellation callback in plugin code threw; the token is still cancelled.
        }
    }

    private async Task SafeDisposeAsync(IAsyncDisposable session, RegisteredActivator registered)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plugin {Id} activator {Activator} failed to dispose a cancelled session.", registered.PluginId, registered.Id);
        }
    }
#pragma warning restore CA1031

    private sealed class WorkspaceScope(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public List<IAsyncDisposable> Sessions { get; } = [];
        public Dictionary<string, ConnectionScope> Connections { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ConnectionScope(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public List<IAsyncDisposable> Sessions { get; } = [];
    }

    private sealed record ActivationContext(string PluginId, PluginLifetime Lifetime, string? WorkspaceId, string? ConnectionId,
        ILogger Logger, CancellationToken Scope) : IPluginActivationContext;
}
