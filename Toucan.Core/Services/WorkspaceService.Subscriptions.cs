using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Toucan.Plugins;

namespace Toucan.Core.Services;

public sealed partial class WorkspaceService
{
    private readonly HashSet<Subscription> _subscriptions = [];
    private ImmutableDictionary<(Guid, string), Unit> _publishedState = ImmutableDictionary<(Guid, string), Unit>.Empty;
    private ImmutableDictionary<(Guid, string), Unit> _publishedBaseline = ImmutableDictionary<(Guid, string), Unit>.Empty;
    private long _publishedRevision;
    private CancellationTokenSource? _sessionLifetime;
    private bool _disposed;

    public Task<IWorkspaceSubscription?> SubscribeAsync(int capacity = 128, CancellationToken cancellationToken = default)
    {
        ValidateCapacity(capacity);
        return Task.Run<IWorkspaceSubscription?>(() =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (_settings is null) return null;
                RefreshContext();
                var subscription = new Subscription(this, SnapshotEntriesLocked()!, capacity);
                _subscriptions.Add(subscription);
                subscription.AttachCancellation(cancellationToken);
                return subscription;
            }
        }, cancellationToken);
    }

    private IdentifiedWorkspaceSnapshot? SnapshotEntriesLocked() => _settings is null ? null :
        new IdentifiedWorkspaceSnapshot(_settings.ProjectPath, _session, _settings.ProjectPath, PrimaryLanguage(), Languages(),
            _state.Values.Select(ToSnapshot).ToImmutableArray(), _revision) { IsClosing = _closing };

    private IdentifiedUnitSnapshot ToSnapshot(Unit unit) => new(unit.Id, unit.Language, unit.Key, unit.Namespace, unit.KeyPath,
        unit.Value, unit.Comment, unit.Approved ? ReviewState.Approved : ReviewState.Draft, Dirty(unit),
        unit.Binding?.DocumentId.Value, unit.Binding?.Location.EscapedPath);

    private static bool WasDirty(Unit unit, ImmutableDictionary<(Guid, string), Unit> baseline) =>
        !baseline.TryGetValue((unit.Id, unit.Language), out var saved) || !Equivalent(unit, saved);

    private void ResetPublicationLocked()
    {
        _publishedState = _state;
        _publishedBaseline = _baseline;
        _publishedRevision = _revision;
    }

    private void PublishChangeLocked(WorkspaceChangeKind kind, WorkspaceChangeOrigin origin, string? originId = null)
    {
        if (_settings is null) { ResetPublicationLocked(); return; }
        if (_subscriptions.Count > 0)
        {
            var units = _publishedState.Keys.Union(_state.Keys).Select(key =>
            {
                _publishedState.TryGetValue(key, out var before);
                _state.TryGetValue(key, out var after);
                return before is not null && after is not null && Equivalent(before, after)
                    && WasDirty(before, _publishedBaseline) == Dirty(after) ? null
                    : new WorkspaceUnitChange(key.Item1, key.Item2, after is null ? null : ToSnapshot(after));
            }).OfType<WorkspaceUnitChange>().ToImmutableArray();
            var change = new WorkspaceChange(_settings.ProjectPath, _session, _publishedRevision, _revision, kind, origin, originId,
                units, PrimaryLanguage(), Languages(), !Same(_state, _baseline), _closing);
            foreach (var subscription in _subscriptions.ToArray())
                if (!subscription.Publish(change)) _subscriptions.Remove(subscription);
        }
        ResetPublicationLocked();
    }

    private void EndSessionLocked()
    {
        StopCommandSessionLocked();
        if (_settings is not null)
        {
            _revision++;
            var closed = new WorkspaceChange(_settings.ProjectPath, _session, _publishedRevision, _revision,
                WorkspaceChangeKind.Closed, WorkspaceChangeOrigin.Lifecycle, null, [], PrimaryLanguage(), Languages(), false, true);
            foreach (var subscription in _subscriptions) subscription.Close(closed);
            _subscriptions.Clear();
        }
        _sessionLifetime?.Cancel();
        _sessionLifetime?.Dispose();
        _sessionLifetime = null;
    }

    private void Detach(Subscription subscription, CancellationToken? cancellation = null)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
            subscription.Finish(cancellation is { } token ? new OperationCanceledException(token) : null, drain: true);
        }
    }

    private static int ValidateCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 4096);
        return capacity;
    }

    private sealed class Subscription : IWorkspaceSubscription
    {
        private readonly WorkspaceService _owner;
        private readonly Channel<WorkspaceChange> _channel;
        private CancellationTokenRegistration _cancellation;
        private bool _finished;
        private int _reading;

        public Subscription(WorkspaceService owner, IdentifiedWorkspaceSnapshot initialSnapshot, int capacity)
        {
            _owner = owner;
            InitialSnapshot = initialSnapshot;
            // Writers also drain on overflow/close, so the reader optimization must remain disabled.
            _channel = Channel.CreateBounded<WorkspaceChange>(new BoundedChannelOptions(capacity)
                { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = true, AllowSynchronousContinuations = false });
        }

        public IdentifiedWorkspaceSnapshot InitialSnapshot { get; }
        public IAsyncEnumerable<WorkspaceChange> Changes => ReadChanges();

        public void AttachCancellation(CancellationToken token)
        {
            _cancellation = token.UnsafeRegister(_ => _owner.Detach(this, token), null);
            if (_finished) _cancellation.Unregister();
        }

        public bool Publish(WorkspaceChange change)
        {
            if (_finished) return false;
            if (_channel.Writer.TryWrite(change)) return true;
            while (_channel.Reader.TryRead(out _)) { }
            _channel.Writer.TryWrite(change with { Kind = WorkspaceChangeKind.ResnapshotRequired,
                Origin = WorkspaceChangeOrigin.Subscription, OriginId = null, Units = [] });
            Finish();
            return false;
        }

        public void Close(WorkspaceChange change)
        {
            if (_finished) return;
            if (!_channel.Writer.TryWrite(change))
            {
                while (_channel.Reader.TryRead(out _)) { }
                _channel.Writer.TryWrite(change);
            }
            Finish();
        }

        public void Finish(Exception? error = null, bool drain = false)
        {
            if (_finished) return;
            _finished = true;
            if (drain) while (_channel.Reader.TryRead(out _)) { }
            _channel.Writer.TryComplete(error);
            // Never wait for a cancellation callback that may itself be waiting on the engine gate.
            _cancellation.Unregister();
        }

        private async IAsyncEnumerable<WorkspaceChange> ReadChanges([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
                throw new InvalidOperationException("A workspace subscription supports one reader.");
            try
            {
                while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    while (_channel.Reader.TryRead(out var change))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return change;
                    }
            }
            finally { await DisposeAsync().ConfigureAwait(false); }
        }

        public ValueTask DisposeAsync()
        {
            _owner.Detach(this);
            return ValueTask.CompletedTask;
        }
    }
}
