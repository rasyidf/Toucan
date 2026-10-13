using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// What a host provides so plugins can read and change its open project. The desktop app implements it over the project it
/// shows; a host with no open project (the CLI today) provides none, and plugins see a workspace that is never open.
/// </summary>
public interface IWorkspaceBackend
{
    bool IsOpen { get; }

    Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <param name="pluginId">The plugin making the edit, for attribution.</param>
    Task<EditResult> ApplyAsync(string pluginId, WorkspaceEdit edit, CancellationToken cancellationToken = default);

    event EventHandler? Changed;
}

/// <summary>A plugin's view of the host's workspace: the same backend, with the plugin's ID on its edits.</summary>
internal sealed class PluginWorkspaceApi : IWorkspaceApi
{
    private readonly string _pluginId;
    private readonly PluginServicesBroker _broker;
    private IWorkspaceBackend? _backend;

    public PluginWorkspaceApi(string pluginId, PluginServicesBroker broker)
    {
        _pluginId = pluginId;
        _broker = broker;
    }

    private EventHandler? _changed;

    public event EventHandler? Changed
    {
        add
        {
            _changed += value;
            _ = Backend; // listening needs the backend, so connect now rather than on first use
        }
        remove => _changed -= value;
    }

    /// <summary>The host's backend, found on first use (not at startup, so a plugin that never touches the project costs nothing).</summary>
    private IWorkspaceBackend? Backend
    {
        get
        {
            if (_backend is not null || !_broker.IsAttached) return _backend;
            lock (_broker)
            {
                if (_backend is null && _broker.TryGet<IWorkspaceBackend>() is { } backend)
                {
                    backend.Changed += (_, e) => _changed?.Invoke(this, e);
                    _backend = backend;
                }
            }
            return _backend;
        }
    }

    public bool IsOpen => Backend?.IsOpen == true;

    public Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default) =>
        Backend is { } backend ? backend.SnapshotAsync(cancellationToken) : Task.FromResult<WorkspaceSnapshot?>(null);

    public Task<EditResult> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Backend is { } backend
            ? backend.ApplyAsync(_pluginId, edit, cancellationToken)
            : Task.FromResult(EditResult.Refused(0, new EditIssue(-1, EditIssueKind.NoProject, "No project is open.")));
    }
}
