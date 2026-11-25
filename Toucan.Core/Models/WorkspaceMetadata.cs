using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Models;

/// <remarks>
/// <see cref="NeedsReview"/> and <see cref="ReviewedSourceHash"/> are written only when set, so sidecars
/// without review state keep their bytes and older readers ignore the extra fields.
/// </remarks>
public sealed record WorkspaceEntryMetadata(EntryId EntryId, string Language, string Comment, bool Approved,
    DateTime? Modified, DateTime? ApprovedAt, ChangeType ChangeType,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool NeedsReview = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ReviewedSourceHash = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Comment.Length == 0 && !Approved && Modified is null && ApprovedAt is null && ChangeType == ChangeType.DirectEdit
        && !NeedsReview && ReviewedSourceHash is null;
}

public sealed record WorkspaceSourceArtifact(string RelativePath, string Version, string Content, SourceTextFormat TextFormat);
public sealed record WorkspaceArtifactSaveTarget(string RelativePath, WorkspaceDocumentSaveOperation Operation,
    string? ExpectedVersion, string? Content, SourceTextFormat TextFormat);
