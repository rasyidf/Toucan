using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Categorizes how a translation item was changed.</summary>
public enum ChangeType
{
    /// <summary>The item was edited directly by a user in the editor.</summary>
    DirectEdit,

    /// <summary>The item value was populated by the pretranslation engine.</summary>
    Suggestion,

    /// <summary>The item was modified as part of a change request workflow.</summary>
    ChangeRequest
}

/// <summary>Audit metadata tracked per translation item.</summary>
public class AuditMetadata
{
    /// <summary>UTC timestamp of the last successful save for this item.</summary>
    public DateTime? LastModifiedUtc { get; set; }

    /// <summary>UTC timestamp when the item was approved.</summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>How the item was last changed. Defaults to DirectEdit for new items.</summary>
    public ChangeType ChangeType { get; set; } = ChangeType.DirectEdit;
}

/// <summary>
/// Records audit metadata (timestamps, change type) on translation items
/// and persists/loads metadata from sidecar files.
/// </summary>
public interface IAuditService
{
    /// <summary>Records that an item was saved (sets LastModifiedUtc to current UTC time).</summary>
    void RecordSave(TranslationItem item);

    /// <summary>Records that an item was approved (sets ApprovedAtUtc to current UTC time).</summary>
    void RecordApproval(TranslationItem item);

    /// <summary>Sets the change type on an item.</summary>
    void SetChangeType(TranslationItem item, ChangeType type);

    /// <summary>Gets audit metadata for an item, or null if none is tracked.</summary>
    AuditMetadata? GetMetadata(TranslationItem item);

    /// <summary>Loads metadata from the sidecar file (.toucan-metadata.json) in the specified folder.</summary>
    void LoadFromSidecar(string folder);

    /// <summary>Persists metadata to the sidecar file (.toucan-metadata.json) in the specified folder.</summary>
    void SaveToSidecar(string folder);

    /// <summary>Clears all tracked metadata (called on project close).</summary>
    void Clear();
}
