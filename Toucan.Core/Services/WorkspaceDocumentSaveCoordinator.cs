using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Opt-in document-content saving. Workspace owns state/session; executor owns operation resources and recovery.</summary>
public sealed class WorkspaceDocumentSaveCoordinator(WorkspaceService workspace, WorkspaceDocumentSavePlanner planner, WorkspaceDocumentSaveExecutor executor)
{
    public async Task<WorkspaceDocumentSaveResult> SaveAsync(WorkspaceSource source,
        IEnumerable<WorkspaceEntryDestination>? destinations = null, IEnumerable<DocumentId>? deletedDocuments = null,
        CancellationToken cancellationToken = default)
        => await SaveCoreAsync(source, destinations, deletedDocuments, null, cancellationToken).ConfigureAwait(false);

    public Task<WorkspaceDocumentSaveResult> SaveDispatchedAsync(WorkspaceSource source, Func<Func<Task>, Task> dispatcher, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(source, null, null, dispatcher, cancellationToken);

    private async Task<WorkspaceDocumentSaveResult> SaveCoreAsync(WorkspaceSource source, IEnumerable<WorkspaceEntryDestination>? destinations,
        IEnumerable<DocumentId>? deletedDocuments, Func<Func<Task>, Task>? dispatcher, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = workspace.CaptureDocumentSnapshot() ?? throw new InvalidOperationException("No workspace is open.");
        var plan = planner.Plan(source, snapshot, destinations, deletedDocuments, cancellationToken);
        var sessionToken = workspace.ValidateDocumentSave(plan);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
        var receipt = await executor.ExecuteAsync(plan, lifetime.Token).ConfigureAwait(false);
        // A committed save stays successful even if close or caller cancellation wins the race with acknowledgment.
        var acknowledged = false;
        if (dispatcher is null) acknowledged = workspace.AcknowledgeDocumentSave(receipt);
        else await dispatcher(() => { acknowledged = workspace.AcknowledgeDocumentSave(receipt); return Task.CompletedTask; }).ConfigureAwait(false);
        return new(receipt, acknowledged);
    }
}
