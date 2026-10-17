using System.Collections.Immutable;
using System.Threading.Channels;
using Toucan.Plugins;

namespace Toucan.Core.Services;

public sealed partial class WorkspaceService
{
    private readonly Channel<QueuedCommand> _commands = Channel.CreateBounded<QueuedCommand>(new BoundedChannelOptions(ValidateCapacity(commandCapacity))
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _commandLifetime = new();
    private readonly object _submissionGate = new();
    private (Guid Id, CancellationToken Token)? _commandSession;
    private Task? _commandWorker;
    [ThreadStatic] private static WorkspaceService? t_executingCommand;

    private void StartCommandSessionLocked()
    {
        lock (_submissionGate) _commandSession = (_session, _sessionLifetime!.Token);
    }

    private void StopCommandSessionLocked()
    {
        lock (_submissionGate) _commandSession = null;
    }

    public Task<EditResult> ApplyAsync(string pluginId, WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var frozen = new WorkspaceEdit { BasedOnRevision = edit.BasedOnRevision, Label = edit.Label, AllowPartial = edit.AllowPartial };
        foreach (var operation in edit.Operations)
            frozen.Add(operation is AddKey add ? add with { Values = add.Values?.ToImmutableDictionary(StringComparer.Ordinal) } : operation);
        return EnqueueAsync((session, token) => Apply(pluginId, frozen, session, token), cancellationToken);
    }

    public Task<EditResult> ApplyEntriesAsync(string pluginId, EntryEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var frozen = edit with { Operations = edit.Operations.ToImmutableArray() };
        return EnqueueAsync((_, token) => ApplyEntries(pluginId, frozen, token), cancellationToken);
    }

    private async Task<EditResult> EnqueueAsync(Func<Guid, CancellationToken, EditResult> execute, CancellationToken cancellationToken)
    {
        QueuedCommand command;
        lock (_submissionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_commandSession is not { } session) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _commandLifetime.Token, session.Token);
            command = new QueuedCommand(session.Id, execute, lifetime);
            _commandWorker ??= Task.Run(ProcessCommandsAsync, CancellationToken.None);
        }
        // Preserve synchronous legacy Changed handlers without making them wait on their own command worker.
        if (ReferenceEquals(t_executingCommand, this))
        {
            using (command)
            {
                if (!command.TryStart()) return await command.Completion.Task.ConfigureAwait(false);
                return command.Execute(command.SessionId, command.Token);
            }
        }
        try { await _commands.Writer.WriteAsync(command, command.Token).ConfigureAwait(false); }
        catch
        {
            command.Dispose();
            throw;
        }
        // Cancellation after acceptance cannot turn an applied transaction into an ambiguous cancelled result.
        return await command.Completion.Task.ConfigureAwait(false);
    }

    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using (command)
                {
                    if (!command.TryStart()) continue;
                    var previous = t_executingCommand;
                    t_executingCommand = this;
                    try { command.Completion.TrySetResult(command.Execute(command.SessionId, command.Token)); }
                    catch (OperationCanceledException) { command.Completion.TrySetCanceled(command.Token); }
                    catch (Exception ex) { command.Completion.TrySetException(ex); }
                    finally { t_executingCommand = previous; }
                }
            }
        }
        finally { _commandLifetime.Dispose(); }
    }

    private sealed class QueuedCommand : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationTokenRegistration _cancellation;
        private int _status;

        public QueuedCommand(Guid sessionId, Func<Guid, CancellationToken, EditResult> execute, CancellationTokenSource lifetime)
        {
            SessionId = sessionId;
            Execute = execute;
            _lifetime = lifetime;
            Token = lifetime.Token;
            _cancellation = Token.UnsafeRegister(_ =>
            {
                if (Interlocked.CompareExchange(ref _status, 2, 0) == 0) Completion.TrySetCanceled(Token);
            }, null);
        }

        public Guid SessionId { get; }
        public Func<Guid, CancellationToken, EditResult> Execute { get; }
        public CancellationToken Token { get; }
        public TaskCompletionSource<EditResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryStart() => Interlocked.CompareExchange(ref _status, 1, 0) == 0;
        public void Dispose() { _cancellation.Unregister(); _lifetime.Dispose(); }
    }

    public void Dispose()
    {
        bool workerStarted;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _closing = true;
            // Keep immutable data readable for plugin teardown during container disposal.
            EndSessionLocked();
            lock (_submissionGate)
            {
                _commandLifetime.Cancel();
                _commands.Writer.TryComplete();
                workerStarted = _commandWorker is not null;
            }
        }
        if (!workerStarted) _commandLifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
