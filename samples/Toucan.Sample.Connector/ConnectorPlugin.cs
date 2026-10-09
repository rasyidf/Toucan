using Toucan.Plugins;

namespace Toucan.Sample.Connector;

/// <summary>
/// Registers settings, a per-project activator and the Pull and Push commands. Registration touches no network:
/// the session is opened when a project is activated, and closed when its scope is cancelled.
/// </summary>
public sealed class ConnectorPlugin : IToucanPlugin
{
    public const string Id = "sample.connector";
    private readonly IRemoteSource _remote;

    public ConnectorPlugin() : this(InMemoryRemote.Shared) { }
    public ConnectorPlugin(IRemoteSource remote) => _remote = remote;

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.SetConfiguration(new ConfigSchema
        {
            Version = 1,
            Fields =
            [
                new ConfigField { Key = "serverUrl", Label = "Server address", Type = ConfigFieldType.Url, Scope = ConfigScope.Connection, Required = true },
                new ConfigField { Key = "apiToken", Label = "API token", Type = ConfigFieldType.Secret, Scope = ConfigScope.Connection, Required = true },
                new ConfigField { Key = "workspaceId", Label = "Connection name", Scope = ConfigScope.Workspace, Default = "default", Pattern = "^[a-z0-9-]+$" },
            ],
        });

        var sessions = new ConnectorSessions();
        context.AddActivator("sessions", new SessionActivator(sessions));
        context.AddCommand(PullCommand.Definition, new PullCommand(context.Services, _remote, sessions));
        context.AddCommand(PushCommand.Definition, new PushCommand(context.Services, _remote, sessions));
    }
}

/// <summary>The projects that have a session open now.</summary>
public sealed class ConnectorSessions
{
    private readonly HashSet<string> _open = [];
    public bool IsOpen(string workspaceId) { lock (_open) return _open.Contains(workspaceId); }
    public bool AnyOpen { get { lock (_open) return _open.Count > 0; } }
    internal void Open(string id) { lock (_open) _open.Add(id); }
    internal void Close(string id) { lock (_open) _open.Remove(id); }
    internal string? First { get { lock (_open) return _open.FirstOrDefault(); } }
}

internal sealed class SessionActivator(ConnectorSessions sessions) : IPluginActivator
{
    public PluginLifetime Lifetime => PluginLifetime.Workspace;

    public Task<IAsyncDisposable?> ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
    {
        var id = context.WorkspaceId ?? throw new InvalidOperationException("A workspace activator needs a workspace.");
        sessions.Open(id);
        return Task.FromResult<IAsyncDisposable?>(new Session(sessions, id));
    }

    private sealed class Session(ConnectorSessions sessions, string id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            sessions.Close(id);
            return ValueTask.CompletedTask;
        }
    }
}

internal static class ConnectorState
{
    public static CommandState Of(ICommandContext context, ConnectorSessions sessions) =>
        !context.HasWorkspace ? new CommandState(CommandAvailability.Unavailable, "Open a project first.")
        : !sessions.AnyOpen ? new CommandState(CommandAvailability.Disconnected, "Not connected.")
        : CommandState.Available;
}
