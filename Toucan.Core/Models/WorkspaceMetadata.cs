using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Models;

public sealed record WorkspaceEntryMetadata(EntryId EntryId, string Language, string Comment, bool Approved,
    DateTime? Modified, DateTime? ApprovedAt, ChangeType ChangeType)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Comment.Length == 0 && !Approved && Modified is null && ApprovedAt is null && ChangeType == ChangeType.DirectEdit;
}

public sealed record WorkspaceSourceArtifact(string RelativePath, string Version, string Content, SourceTextFormat TextFormat);
public sealed record WorkspaceArtifactSaveTarget(string RelativePath, WorkspaceDocumentSaveOperation Operation,
    string? ExpectedVersion, string? Content, SourceTextFormat TextFormat);
