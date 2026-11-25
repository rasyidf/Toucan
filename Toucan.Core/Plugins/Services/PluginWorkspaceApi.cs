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

public interface IIdentifiedWorkspaceBackend : IWorkspaceBackend
{
    Task<IdentifiedWorkspaceSnapshot?> SnapshotEntriesAsync(CancellationToken cancellationToken = default);
    Task<EditResult> ApplyEntriesAsync(string pluginId, EntryEdit edit, CancellationToken cancellationToken = default);
}

public interface IWorkspaceSubscriptionBackend : IIdentifiedWorkspaceBackend
{
    Task<IWorkspaceSubscription?> SubscribeAsync(int capacity = 128, CancellationToken cancellationToken = default);
}

/// <summary>
/// A plugin's view of the host's workspace: the same backend, with the plugin's ID on its edits. Review states newer than
/// the plugin's API version are shown as the closest state it knows (<see cref="ReviewState.NeedsReview"/> as Draft before 1.3).
/// </summary>
internal sealed class PluginWorkspaceApi : IWorkspaceSubscriptionApi
{
    private readonly string _pluginId;
    private readonly PluginServicesBroker _broker;
    private readonly Version _apiVersion;
    private IWorkspaceBackend? _backend;

    public PluginWorkspaceApi(string pluginId, PluginServicesBroker broker, Version? apiVersion = null)
    {
        _pluginId = pluginId;
        _broker = broker;
        _apiVersion = apiVersion ?? PluginApi.Current;
    }

    private bool HidesNeedsReview => _apiVersion < ReviewStateMapping.NeedsReviewSince;

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

    public async Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (Backend is not { } backend) return null;
        var snapshot = await backend.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !HidesNeedsReview || !snapshot.Units.Any(u => u.Review == ReviewState.NeedsReview)) return snapshot;
        return new WorkspaceSnapshot(snapshot.WorkspaceId, snapshot.ProjectPath, snapshot.PrimaryLanguage, snapshot.Languages,
            [.. snapshot.Units.Select(u => u.Review == ReviewState.NeedsReview ? u with { Review = ReviewState.Draft } : u)], snapshot.Revision);
    }

    public Task<EditResult> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Backend is { } backend
            ? backend.ApplyAsync(_pluginId, edit, cancellationToken)
            : Task.FromResult(EditResult.Refused(0, new EditIssue(-1, EditIssueKind.NoProject, "No project is open.")));
    }

    public async Task<IdentifiedWorkspaceSnapshot?> SnapshotEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (Backend is not IIdentifiedWorkspaceBackend backend) return null;
        var snapshot = await backend.SnapshotEntriesAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is null ? null : ForApi(snapshot);
    }

    public Task<EditResult> ApplyEntriesAsync(EntryEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Backend is IIdentifiedWorkspaceBackend backend ? backend.ApplyEntriesAsync(_pluginId, edit, cancellationToken)
            : Task.FromResult(EditResult.Refused(0, new EditIssue(-1, EditIssueKind.NoProject, "No identified workspace is available.")));
    }

    public async Task<IWorkspaceSubscription?> SubscribeAsync(int capacity = 128, CancellationToken cancellationToken = default)
    {
        if (Backend is not IWorkspaceSubscriptionBackend backend) return null;
        var subscription = await backend.SubscribeAsync(capacity, cancellationToken).ConfigureAwait(false);
        return subscription is null || !HidesNeedsReview ? subscription : new DowngradedSubscription(subscription, this);
    }

    private IdentifiedWorkspaceSnapshot ForApi(IdentifiedWorkspaceSnapshot snapshot) =>
        !HidesNeedsReview || !snapshot.Units.Any(u => u.Review == ReviewState.NeedsReview) ? snapshot
            : snapshot with { Units = [.. snapshot.Units.Select(ForApi)] };

    private IdentifiedUnitSnapshot ForApi(IdentifiedUnitSnapshot unit) =>
        unit.Review == ReviewState.NeedsReview && HidesNeedsReview ? unit with { Review = ReviewState.Draft } : unit;

    private WorkspaceChange ForApi(WorkspaceChange change) =>
        !change.Units.Any(u => u.Unit?.Review == ReviewState.NeedsReview) ? change
            : change with { Units = [.. change.Units.Select(u => u.Unit is { } unit ? u with { Unit = ForApi(unit) } : u)] };

    /// <summary>The backend's subscription with review states a pre-1.3 plugin knows.</summary>
    private sealed class DowngradedSubscription(IWorkspaceSubscription inner, PluginWorkspaceApi api) : IWorkspaceSubscription
    {
        public IdentifiedWorkspaceSnapshot InitialSnapshot { get; } = api.ForApi(inner.InitialSnapshot);

        public IAsyncEnumerable<WorkspaceChange> Changes => Map(inner.Changes);

        private async IAsyncEnumerable<WorkspaceChange> Map(IAsyncEnumerable<WorkspaceChange> changes)
        {
            await foreach (var change in changes.ConfigureAwait(false)) yield return api.ForApi(change);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
