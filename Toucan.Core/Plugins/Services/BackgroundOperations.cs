using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>A background operation as the host sees it. Observable, so the status bar can bind to it.</summary>
public sealed partial class BackgroundOperation : ObservableObject, IOperationHandle
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal BackgroundOperation(string source, OperationOptions options)
    {
        Id = Guid.NewGuid();
        Source = source;
        Title = options.Title;
        IsCancellable = options.IsCancellable;
        CancelWithWorkspace = options.CancelWithWorkspace;
    }

    public Guid Id { get; }
    public string Source { get; }
    public string Title { get; }
    public bool IsCancellable { get; }
    public bool CancelWithWorkspace { get; }

    [ObservableProperty] private OperationStatus status = OperationStatus.Running;
    [ObservableProperty] private string? failureMessage;
    [ObservableProperty] private string? message;
    [ObservableProperty] private double? fraction;

    public Task Completion => _done.Task;

    internal CancellationToken Token => _cts.Token;

    /// <summary>Asks the work to stop. Honoured when it observes its token.</summary>
    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It already finished.
        }
    }

    internal void Finish(OperationStatus finalStatus, string? failure)
    {
        FailureMessage = failure;
        Status = finalStatus;
        _cts.Dispose();
        _done.TrySetResult();
    }
}

/// <summary>Runs plugin work in the background, tracks it for the UI and keeps it from outliving its project.</summary>
public interface IBackgroundOperationService
{
    /// <summary>Operations that have not ended.</summary>
    IReadOnlyList<BackgroundOperation> Active { get; }

    /// <summary>Raised when an operation starts or ends. Progress changes are raised on the operation itself.</summary>
    event EventHandler? Changed;

    BackgroundOperation Start(string source, OperationOptions options, Func<IOperationContext, CancellationToken, Task> work);

    /// <summary>Cancels every operation that asked to end with the project; call when a project closes.</summary>
    void CancelForWorkspaceClose();
}

public sealed class BackgroundOperationService(IDiagnosticsService? diagnostics = null) : IBackgroundOperationService
{
    private readonly object _gate = new();
    private readonly List<BackgroundOperation> _active = [];

    public IReadOnlyList<BackgroundOperation> Active { get { lock (_gate) return [.. _active]; } }

    public event EventHandler? Changed;

    // Plugin work is untrusted: whatever it throws ends that operation as failed and never escapes.
#pragma warning disable CA1031
    public BackgroundOperation Start(string source, OperationOptions options, Func<IOperationContext, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(work);

        var operation = new BackgroundOperation(source, options);
        lock (_gate) _active.Add(operation);
        Changed?.Invoke(this, EventArgs.Empty);

        _ = Task.Run(async () =>
        {
            var status = OperationStatus.Completed;
            string? failure = null;
            try
            {
                await work(new Context(operation), operation.Token).ConfigureAwait(false);
                if (operation.Token.IsCancellationRequested) status = OperationStatus.Cancelled;
            }
            catch (OperationCanceledException)
            {
                status = OperationStatus.Cancelled;
            }
            catch (Exception ex)
            {
                status = OperationStatus.Failed;
                failure = diagnostics?.Redact($"{ex.GetType().Name}: {ex.Message}") ?? $"{ex.GetType().Name}: {ex.Message}";
                diagnostics?.Write(source, DiagnosticLevel.Error, $"Operation '{options.Title}' failed: {ex}");
            }

            operation.Finish(status, failure);
            lock (_gate) _active.Remove(operation);
            Changed?.Invoke(this, EventArgs.Empty);
        });
        return operation;
    }
#pragma warning restore CA1031

    public void CancelForWorkspaceClose()
    {
        foreach (var operation in Active.Where(o => o.CancelWithWorkspace)) operation.Cancel();
    }

    private sealed class Context(BackgroundOperation operation) : IOperationContext
    {
        public void Report(string? message, double? fraction = null)
        {
            operation.Message = message;
            operation.Fraction = fraction is { } f ? Math.Clamp(f, 0, 1) : null;
        }
    }
}

/// <summary>Plugin-facing operations: the same service, with the plugin's ID as the source.</summary>
internal sealed class PluginOperations(string pluginId, IBackgroundOperationService service) : IBackgroundOperations
{
    public IReadOnlyList<IOperationHandle> Active => [.. service.Active.Where(o => o.Source == pluginId)];

    public IOperationHandle Start(OperationOptions options, Func<IOperationContext, CancellationToken, Task> work) => service.Start(pluginId, options, work);
}
