using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>
/// What a plugin sees of sync: status, plan summaries and delivery state, plus the right to ask for a plan or a run. It
/// reaches the coordinator lazily, because plugins load before the container is connected. It never exposes baselines,
/// journals, credentials or conflict contents.
/// </summary>
internal sealed class PluginSourceSyncApi(Func<SyncCoordinator> coordinator) : ISourceSyncApi, IDisposable
{
    private SyncCoordinator? _attached;
    private EventHandler? _changed;

    private SyncCoordinator Coordinator
    {
        get
        {
            if (_attached is not null) return _attached;
            _attached = coordinator();
            _attached.DeliveryChanged += Forward;
            _attached.StatusChanged += Forward;
            return _attached;
        }
    }

    public event EventHandler? Changed
    {
        add { _ = Coordinator; _changed += value; }
        remove => _changed -= value;
    }

    public IReadOnlyList<SourceSyncStatus> Connections => [.. Coordinator.Statuses.Select(Map)];

    public SourceSyncStatus? Status(string connectionId) => Coordinator.Status(connectionId) is { } s ? Map(s) : null;

    public SourceSyncPlanSummary? LastPlan(string connectionId) => Coordinator.LastPlan(connectionId) is { } p ? Summarize(p) : null;

    public SourceDeliveryState? DeliveryOf(Guid entryId, string language) => Coordinator.DeliveryOf(entryId, language);

    public async Task<SourceSyncPlanSummary> RequestPlanAsync(string connectionId, SourceSyncKind kind, CancellationToken cancellationToken = default) =>
        Summarize(await Coordinator.PlanAsync(connectionId, ToKind(kind), cancellationToken).ConfigureAwait(false));

    public async Task<SourceSyncRunSummary> RunAsync(string connectionId, SourceSyncKind kind, CancellationToken cancellationToken = default)
    {
        var result = await Coordinator.RunAsync(connectionId, ToKind(kind), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new(connectionId, result.Pull?.Applied + result.Pull?.Created ?? 0, result.Push?.Confirmed ?? 0,
            (result.Push?.Failed ?? 0) + (result.Push?.Unknown ?? 0), result.Plan.Conflicts.Count + (result.Push?.Conflicts.Count ?? 0), result.Message);
    }

    public void Dispose()
    {
        if (_attached is null) return;
        _attached.DeliveryChanged -= Forward;
        _attached.StatusChanged -= Forward;
    }

    private void Forward(object? sender, EventArgs e) => _changed?.Invoke(this, EventArgs.Empty);

    private static SyncPlanKind ToKind(SourceSyncKind kind) => kind switch { SourceSyncKind.Pull => SyncPlanKind.Pull, SourceSyncKind.Push => SyncPlanKind.Push, _ => SyncPlanKind.Sync };

    private static SourceSyncStatus Map(SyncStatus s) => new(s.ConnectionId, s.Name, s.State switch
    {
        SyncRunState.Planning => SourceSyncState.Planning,
        SyncRunState.Pulling => SourceSyncState.Pulling,
        SyncRunState.Pushing => SourceSyncState.Pushing,
        _ => SourceSyncState.Idle,
    }, s.Message, s.LastPull, s.LastPush, s.PendingChanges, s.Conflicts, s.RemoteChangesAvailable);

    private static SourceSyncPlanSummary Summarize(SyncPlan p) => new(p.ConnectionId, p.Kind switch
    {
        SyncPlanKind.Pull => SourceSyncKind.Pull,
        SyncPlanKind.Push => SourceSyncKind.Push,
        _ => SourceSyncKind.Sync,
    }, p.PullCount, p.PushCount, p.Conflicts.Count, p.Notices.Count, p.IsFirstSync, p.RemoteReadComplete);
}
