namespace Toucan.Core.Models;

/// <summary>A document changed after it was loaded or a create destination already exists.</summary>
public sealed class WorkspaceSourceConflictException : IOException
{
    public WorkspaceSourceConflictException() : this("Source changed since the save was prepared.") { }
    public WorkspaceSourceConflictException(string message) : base(message) { }
    public WorkspaceSourceConflictException(string message, Exception innerException) : base(message, innerException) { }
    public WorkspaceSourceConflictException(string relativePath, string? expectedVersion, string? actualVersion)
        : base($"Source '{relativePath}' changed since the save was prepared.")
    { RelativePath = relativePath; ExpectedVersion = expectedVersion; ActualVersion = actualVersion; }
    public string? RelativePath { get; }
    public string? ExpectedVersion { get; }
    public string? ActualVersion { get; }
}

/// <summary>Recovery was incomplete. The journal remains available and externally changed targets are preserved.</summary>
public sealed class WorkspaceSaveRecoveryException : IOException
{
    public WorkspaceSaveRecoveryException() : this("Document save failed and recovery is incomplete.") { }
    public WorkspaceSaveRecoveryException(string message) : base(message) { }
    public WorkspaceSaveRecoveryException(string message, Exception innerException) : base(message, innerException) { }
    public WorkspaceSaveRecoveryException(Exception failure, Toucan.Core.Services.SaveRollbackResult rollback)
        : base("Document save failed and recovery is incomplete. Resolve the retained recovery journal before saving again.", failure)
    { Rollback = rollback; }
    public Toucan.Core.Services.SaveRollbackResult? Rollback { get; }
}

/// <summary>Returned only after successful execution; Source contains the exact versions and bindings now saved.</summary>
public sealed class WorkspaceDocumentSaveReceipt
{
    internal WorkspaceDocumentSaveReceipt(WorkspaceDocumentSavePlan plan, WorkspaceSource source)
    {
        Plan = plan;
        Source = source;
    }
    public WorkspaceDocumentSavePlan Plan { get; }
    public WorkspaceSource Source { get; }
}

public sealed record WorkspaceDocumentSaveResult(WorkspaceDocumentSaveReceipt Receipt, bool Acknowledged);
