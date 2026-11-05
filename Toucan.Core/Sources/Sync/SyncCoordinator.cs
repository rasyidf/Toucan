using System.Collections.Concurrent;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

public enum SyncRunState
{
    Idle,
    Planning,
    Pulling,
    Pushing,
}

/// <summary>A connection's sync status for the UI, the CLI and plugins.</summary>
public sealed record SyncStatus(string ConnectionId, string Name, SyncRunState State, string Message, DateTimeOffset? LastPull, DateTimeOffset? LastPush,
    int PendingChanges, int Conflicts, bool RemoteChangesAvailable, bool NeedsSignIn = false, bool PartialFailure = false);

public sealed class SyncStatusChangedEventArgs(SyncStatus status) : EventArgs
{
    public SyncStatus Status { get; } = status;
}

/// <param name="Applied">False when the plan was not applied, for example because conflicts need the user first.</param>
public sealed record SyncRunResult(string ConnectionId, SyncPlan Plan, PullResult? Pull, PushResult? Push, bool Applied, string? Message)
{
    public bool HasConflicts => Plan.Conflicts.Count > 0 || Push?.Conflicts.Count > 0;
    public bool Succeeded => Applied && Pull?.Succeeded != false && Push?.Succeeded != false;
}

/// <summary>
/// The one entry point to sync (design 7.7): plans, applies and reports per connection. One run per connection at a time;
/// another request for the same connection while one is active joins it. Different connections run independently, except
/// <see cref="RunAllAsync"/>, which goes one at a time so a later connection sees the earlier one's pull as local state.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private readonly WorkspaceService _workspace;
    private readonly ISourceConnectionService _connections;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly SyncPullApplier _puller;
    private readonly SyncExecutor _executor;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<SyncRunResult>> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyncStatus> _status = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyncPlan> _lastPlan = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RemoteUnit>> _lastRemote = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SourceProjectInfo> _described = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<(Guid, string), SourceDeliveryState>> _delivery = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _schedules = new(StringComparer.Ordinal);
    private IList<SourceConnectionSettings> _settings = [];
    private string? _root;
    private bool _refreshing;

    public SyncCoordinator(WorkspaceService workspace, ISourceConnectionService connections, ISourceAdapterCatalog adapters,
        SyncPullApplier? puller = null, SyncExecutor? executor = null, TimeProvider? time = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _time = time ?? TimeProvider.System;
        _puller = puller ?? new SyncPullApplier(workspace, _time);
        _executor = executor ?? new SyncExecutor(_time);
        _workspace.Changed += OnWorkspaceChanged;
    }

    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    /// <summary>Raised when delivery states may have changed.</summary>
    public event EventHandler? DeliveryChanged;

    public string? ProjectRoot { get { lock (_gate) return _root; } }

    public IReadOnlyList<SyncStatus> Statuses { get { lock (_gate) return [.. _status.Values]; } }

    public SyncStatus? Status(string connectionId) { lock (_gate) return _status.GetValueOrDefault(connectionId); }

    public SyncPlan? LastPlan(string connectionId) { lock (_gate) return _lastPlan.GetValueOrDefault(connectionId); }

    /// <summary>Binds the coordinator to a project: its folder (where sync state lives) and its connections.</summary>
    public void Open(string projectRoot, IList<SourceConnectionSettings> connections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(connections);
        Close();
        lock (_gate)
        {
            _root = Path.GetFullPath(projectRoot);
            _settings = connections;
            foreach (var c in connections.Where(c => !string.IsNullOrWhiteSpace(c.Adapter)))
                _status[c.Id] = Idle(c, SyncStatus(c.Id));
        }
        _workspace.DeliveryProvider = DeliveryOf;
        RefreshDelivery(publish: false);
    }

    public void Close()
    {
        StopScheduledChecks();
        lock (_gate)
        {
            _root = null;
            _settings = [];
            _status.Clear();
            _lastPlan.Clear();
            _lastRemote.Clear();
            _described.Clear();
            _delivery.Clear();
        }
        _workspace.DeliveryProvider = null;
    }

    public void Dispose()
    {
        _workspace.Changed -= OnWorkspaceChanged;
        Close();
    }

    public SourceConnectionSettings Connection(string connectionId)
    {
        lock (_gate)
            return _settings.FirstOrDefault(c => c.Id == connectionId) ?? throw new InvalidOperationException($"The project has no connection {connectionId}.");
    }

    /// <summary>Problems with the set of connections: two primary connections for the same content, or two with one ID.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<SourceConnectionSettings> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var problems = new List<string>();
        foreach (var dup in connections.GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Two connections use the ID {dup.Key}.");
        var primary = connections.Where(c => c.Role == SourceRoles.Primary).ToList();
        for (var i = 0; i < primary.Count; i++)
            for (var j = i + 1; j < primary.Count; j++)
                if (Overlaps(primary[i], primary[j]))
                    problems.Add($"{primary[i].Name ?? primary[i].Id} and {primary[j].Name ?? primary[j].Id} are both primary for the same content. Only one connection can be primary for a translation.");
        return problems;
    }

    private static bool Overlaps(SourceConnectionSettings a, SourceConnectionSettings b)
    {
        if (!a.Languages.Keys.Intersect(b.Languages.Keys, StringComparer.Ordinal).Any()) return false;
        if (a.Resources.Count == 0 || b.Resources.Count == 0) return true;
        return a.Resources.Any(x => b.Resources.Any(y => string.Equals(x.Package, y.Package, StringComparison.Ordinal)));
    }

    // ───────────── planning and running ─────────────

    /// <summary>Reads the service and plans. Changes nothing.</summary>
    public async Task<SyncPlan> PlanAsync(string connectionId, SyncPlanKind kind, CancellationToken cancellationToken = default)
    {
        var connection = Connection(connectionId);
        SetState(connection, SyncRunState.Planning, "Reading " + (connection.Name ?? connection.Id));
        try
        {
            var (plan, _) = await PlanCoreAsync(connection, kind, cancellationToken).ConfigureAwait(false);
            SetIdle(connection, plan.HasChanges ? "Changes are ready." : "Up to date.", remoteChanges: plan.PullCount > 0 || plan.HasConflicts);
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(connection, ex);
            throw;
        }
    }

    /// <summary>Applies a plan the user has settled. Conflicts still in it are left as they are.</summary>
    public async Task<SyncRunResult> ApplyAsync(SyncPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var connection = Connection(plan.ConnectionId);
        return await ApplyCoreAsync(connection, plan, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Plans and applies in one step. With conflicts it applies the rest and reports them, unless <paramref name="requireNoConflicts"/>
    /// is set, in which case it applies nothing. Joins a run that is already active for the connection.
    /// </summary>
    public Task<SyncRunResult> RunAsync(string connectionId, SyncPlanKind kind, IEnumerable<SyncResolution>? resolutions = null,
        bool requireNoConflicts = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_running.TryGetValue(connectionId, out var active)) return active;
            var task = RunCoreAsync(connectionId, kind, resolutions, requireNoConflicts, progress, cancellationToken);
            _running[connectionId] = task;
            _ = task.ContinueWith(_ => { lock (_gate) _running.Remove(connectionId); }, TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>Runs as a background operation with progress and cancel, joining an active run.</summary>
    public IOperationHandle RunInBackground(IBackgroundOperations operations, string connectionId, SyncPlanKind kind, IEnumerable<SyncResolution>? resolutions = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var name = Connection(connectionId).Name ?? connectionId;
        var title = kind switch { SyncPlanKind.Pull => $"Pulling from {name}", SyncPlanKind.Push => $"Pushing to {name}", _ => $"Syncing with {name}" };
        return operations.Start(new() { Title = title }, async (context, token) =>
        {
            var progress = new Progress<SyncProgress>(p => context.Report(p.Message, p.Total > 0 ? (double)p.Done / p.Total : null));
            await RunAsync(connectionId, kind, resolutions, false, progress, token).ConfigureAwait(false);
        });
    }

    /// <summary>One connection after another, in project order.</summary>
    public async Task<IReadOnlyList<SyncRunResult>> RunAllAsync(SyncPlanKind kind, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SourceConnectionSettings> all;
        lock (_gate) all = [.. _settings.Where(c => !string.IsNullOrWhiteSpace(c.Adapter))];
        if (Validate(all) is { Count: > 0 } problems) throw new InvalidOperationException(string.Join(" ", problems));
        var results = new List<SyncRunResult>();
        foreach (var connection in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { results.Add(await RunAsync(connection.Id, kind, cancellationToken: cancellationToken).ConfigureAwait(false)); }
            catch (SourceException) { /* status already says why; the next connection still runs */ }
        }
        return results;
    }

    private async Task<SyncRunResult> RunCoreAsync(string connectionId, SyncPlanKind kind, IEnumerable<SyncResolution>? resolutions, bool requireNoConflicts,
        IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        var connection = Connection(connectionId);
        try
        {
            SetState(connection, SyncRunState.Planning, "Reading " + (connection.Name ?? connection.Id));
            var (plan, _) = await PlanCoreAsync(connection, kind, cancellationToken).ConfigureAwait(false);
            if (resolutions is not null) plan = SyncPlanResolver.Resolve(plan, resolutions);
            if (requireNoConflicts && plan.HasConflicts)
            {
                SetIdle(connection, $"{plan.Conflicts.Count} conflicts need your decision.", remoteChanges: true);
                return new(connectionId, plan, null, null, false, "Conflicts need a decision first.");
            }
            return await ApplyCoreAsync(connection, plan, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(connection, ex);
            throw;
        }
    }

    private async Task<SyncRunResult> ApplyCoreAsync(SourceConnectionSettings connection, SyncPlan plan, IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        var store = Store(connection);
        PullResult? pulled = null;
        PushResult? pushed = null;
        try
        {
            if (plan.Kind != SyncPlanKind.Push)
            {
                SetState(connection, SyncRunState.Pulling, "Applying changes from " + (connection.Name ?? connection.Id));
                pulled = await _puller.ApplyAsync(plan, connection, store, ct).ConfigureAwait(false);
            }
            if (plan.Kind != SyncPlanKind.Pull && plan.Push.Any(c => c.Include))
            {
                SetState(connection, SyncRunState.Pushing, "Sending changes to " + (connection.Name ?? connection.Id));
                var session = await UnitSessionAsync(connection.Id, ct).ConfigureAwait(false);
                var (caps, limits) = await CapabilitiesAsync(connection, session, ct).ConfigureAwait(false);
                pushed = await _executor.PushAsync(plan, connection, session, caps, limits, store, progress, ct).ConfigureAwait(false);
            }
            if (connection.Role == SourceRoles.Primary && _lastRemote.GetValueOrDefault(connection.Id) is { } remote) store.SaveUnitCache(remote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(connection, ex);
            throw;
        }
        var failed = pushed is { Succeeded: false } || pulled is { Succeeded: false };
        var message = failed ? "Some changes were not applied." : plan.Conflicts.Count > 0 ? $"{plan.Conflicts.Count} conflicts left." : "Done.";
        RefreshDelivery(publish: true);
        SetIdle(connection, message, remoteChanges: plan.Conflicts.Count > 0, partial: failed);
        return new(connection.Id, plan, pulled, pushed, true, message);
    }

    private async Task<(SyncPlan Plan, List<RemoteUnit> Remote)> PlanCoreAsync(SourceConnectionSettings connection, SyncPlanKind kind, CancellationToken ct)
    {
        var session = await UnitSessionAsync(connection.Id, ct).ConfigureAwait(false);
        var (caps, _) = await CapabilitiesAsync(connection, session, ct).ConfigureAwait(false);
        var store = Store(connection);

        // Finish what an earlier run left unclear before reading, so the plan sees its outcome.
        var journal = store.LoadJournal();
        if (journal.Entries.Any(e => e.State is JournalState.InFlight or JournalState.Unknown or JournalState.Planned))
            await _executor.ResumeAsync(connection, session, caps, (await CapabilitiesAsync(connection, session, ct).ConfigureAwait(false)).Limits, store, null, ct).ConfigureAwait(false);

        var (remote, complete) = await ReadRemoteAsync(session, connection, ct).ConfigureAwait(false);
        var snapshot = await _workspace.SnapshotEntriesAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("No project is open.");
        var plan = SyncPlanner.Plan(new(connection.Id, connection, kind, store.LoadBaseline(), SyncMapping.LocalUnits(snapshot, [.. connection.Languages.Keys]),
            remote, complete, caps, snapshot.Revision, _time.GetUtcNow()));
        lock (_gate)
        {
            _lastPlan[connection.Id] = plan;
            _lastRemote[connection.Id] = remote;
        }
        return (plan, remote);
    }

    private async Task<(List<RemoteUnit> Units, bool Complete)> ReadRemoteAsync(IUnitSourceSession session, SourceConnectionSettings connection, CancellationToken ct)
    {
        var units = new List<RemoteUnit>();
        var complete = false;
        var resources = connection.Resources.Count == 0 ? null : (IReadOnlyList<string>)[.. connection.Resources.Select(r => r.Remote)];
        var languages = connection.Languages.Count == 0 ? null : (IReadOnlyList<string>)[.. connection.Languages.Values.Distinct(StringComparer.Ordinal)];
        await foreach (var page in session.ReadUnitsAsync(new(resources, languages), ct).ConfigureAwait(false))
        {
            units.AddRange(page.Units);
            complete = page.IsComplete;
        }
        return (units, complete);
    }

    private async Task<IUnitSourceSession> UnitSessionAsync(string connectionId, CancellationToken ct) =>
        await _connections.GetSessionAsync(connectionId, ct).ConfigureAwait(false) as IUnitSourceSession
        ?? throw new SourceException(new SourceError(SourceErrorKind.Unsupported, "This connection stores whole files; refresh its files instead of syncing units."));

    private async Task<(SourceCapabilities Capabilities, SourceLimits? Limits)> CapabilitiesAsync(SourceConnectionSettings connection, IContentSourceSession session, CancellationToken ct)
    {
        SourceProjectInfo? info;
        lock (_gate) _described.TryGetValue(connection.Id, out info);
        if (info is null)
        {
            info = await session.DescribeAsync(ct).ConfigureAwait(false);
            lock (_gate) _described[connection.Id] = info;
        }
        var declared = _adapters.Find(connection.Adapter ?? string.Empty)?.Descriptor.Capabilities ?? SourceCapabilities.None;
        return (declared & info.Permissions, info.Limits);
    }

    private SyncStateStore Store(SourceConnectionSettings connection)
    {
        string root;
        lock (_gate) root = _root ?? throw new InvalidOperationException("No project is open.");
        return new SyncStateStore(root, connection.Id, _time);
    }

    // ───────────── scheduled checks ─────────────

    /// <summary>Opt-in: read and plan on an interval without applying, so the UI can show that remote changes are waiting.</summary>
    public void StartScheduledCheck(string connectionId, TimeSpan interval)
    {
        if (interval < TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(interval), "Check at most every 30 seconds.");
        _ = Connection(connectionId);
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_schedules.Remove(connectionId, out var old)) old.Cancel();
            _schedules[connectionId] = cts;
        }
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval, _time);
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    lock (_gate) if (_running.ContainsKey(connectionId)) continue;
                    try { await PlanAsync(connectionId, SyncPlanKind.Pull, cts.Token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is SourceException or HttpRequestException) { /* the status carries the reason */ }
                }
            }
            catch (OperationCanceledException) { }
        }, cts.Token);
    }

    public void StopScheduledChecks()
    {
        List<CancellationTokenSource> all;
        lock (_gate) { all = [.. _schedules.Values]; _schedules.Clear(); }
        foreach (var cts in all) { cts.Cancel(); cts.Dispose(); }
    }

    // ───────────── status and delivery ─────────────

    public SourceDeliveryState? DeliveryOf(Guid entryId, string language)
    {
        SourceDeliveryState? state = null;
        lock (_gate)
            foreach (var map in _delivery.Values)
                if (map.TryGetValue((entryId, language), out var s)) state = state is null ? s : SyncDelivery.Worst(state.Value, s);
        return state;
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => RefreshDelivery(publish: true);

    /// <summary>Recomputes delivery states from the journals, baselines and the project, and tells subscribers what moved.</summary>
    public void RefreshDelivery(bool publish)
    {
        string? root;
        List<SourceConnectionSettings> connections;
        lock (_gate)
        {
            if (_refreshing || _root is null) return;
            _refreshing = true;
            root = _root;
            connections = [.. _settings.Where(c => !string.IsNullOrWhiteSpace(c.Adapter))];
        }
        try
        {
            var snapshot = _workspace.SnapshotEntriesAsync().GetAwaiter().GetResult();
            if (snapshot is null) return;
            var changed = new HashSet<(Guid, string)>();
            var pending = new Dictionary<string, int>();
            foreach (var connection in connections)
            {
                var store = new SyncStateStore(root, connection.Id, _time);
                var local = SyncMapping.LocalUnits(snapshot, [.. connection.Languages.Keys]);
                var next = SyncDelivery.Compute(store.LoadBaseline(), store.LoadJournal(), local);
                lock (_gate)
                {
                    var before = _delivery.GetValueOrDefault(connection.Id);
                    foreach (var (key, state) in next)
                        if (before is null || !before.TryGetValue(key, out var old) || old != state) changed.Add(key);
                    if (before is not null) foreach (var key in before.Keys.Where(k => !next.ContainsKey(k))) changed.Add(key);
                    _delivery[connection.Id] = next;
                }
                pending[connection.Id] = next.Count(p => p.Value != SourceDeliveryState.Synced);
            }
            foreach (var (id, count) in pending)
            {
                SyncStatus? status;
                lock (_gate) status = _status.GetValueOrDefault(id);
                if (status is not null && status.PendingChanges != count) Publish(status with { PendingChanges = count });
            }
            if (publish && changed.Count > 0)
            {
                _workspace.PublishDeliveryChanged(changed);
                DeliveryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { /* delivery is advisory; a failed refresh keeps the last states */ }
        finally { lock (_gate) _refreshing = false; }
    }

    private SyncStatus? SyncStatus(string id) { lock (_gate) return _status.GetValueOrDefault(id); }

    private SyncStatus Idle(SourceConnectionSettings c, SyncStatus? old)
    {
        var baseline = _root is null ? null : new SyncStateStore(_root, c.Id, _time).LoadBaseline();
        return new(c.Id, c.Name ?? c.Id, SyncRunState.Idle, old?.Message ?? string.Empty, baseline?.LastPull, baseline?.LastPush, old?.PendingChanges ?? 0, old?.Conflicts ?? 0, old?.RemoteChangesAvailable ?? false);
    }

    private void SetState(SourceConnectionSettings c, SyncRunState state, string message)
    {
        var old = Status(c.Id) ?? Idle(c, null);
        Publish(old with { State = state, Message = message, NeedsSignIn = false });
    }

    private void SetIdle(SourceConnectionSettings c, string message, bool remoteChanges, bool partial = false)
    {
        var baseline = _root is null ? null : new SyncStateStore(_root, c.Id, _time).LoadBaseline();
        var plan = LastPlan(c.Id);
        var old = Status(c.Id) ?? Idle(c, null);
        Publish(old with
        {
            State = SyncRunState.Idle, Message = message, LastPull = baseline?.LastPull, LastPush = baseline?.LastPush,
            Conflicts = plan?.Conflicts.Count ?? 0, RemoteChangesAvailable = remoteChanges, PartialFailure = partial, NeedsSignIn = false,
        });
    }

    private void Fail(SourceConnectionSettings c, Exception ex)
    {
        var old = Status(c.Id) ?? Idle(c, null);
        var auth = ex is SourceException { Error.Kind: SourceErrorKind.Authentication };
        Publish(old with { State = SyncRunState.Idle, Message = auth ? "Sign in again to continue." : ex.Message, NeedsSignIn = auth, PartialFailure = !auth });
    }

    private void Publish(SyncStatus status)
    {
        lock (_gate) _status[status.ConnectionId] = status;
        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs(status));
    }
}
