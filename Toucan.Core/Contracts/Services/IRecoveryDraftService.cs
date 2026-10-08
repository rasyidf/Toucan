using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>One unsaved edit. <see cref="BaseValue"/> is the value on disk when the edit started; null means a new entry.</summary>
public sealed record RecoveryDraftEntry(string Language, string Namespace, string? BaseValue, string Value, string? BaseComment, string? Comment);

/// <summary>A key removed since the last save. <see cref="BaseValue"/> is what it held, so a change made on disk meanwhile is noticed.</summary>
public sealed record RecoveryDraftDeletion(string Language, string Namespace, string BaseValue);

/// <summary>An approval toggled since the last save.</summary>
public sealed record RecoveryDraftApproval(string Language, string Namespace, bool Approved);

/// <summary>
/// Unsaved changes of one project, kept outside the project folder so a crash does not lose them. A renamed key
/// shows up as a deletion plus a new entry.
/// </summary>
public sealed record RecoveryDraft(string ProjectPath, DateTime SavedAtUtc, IReadOnlyList<RecoveryDraftEntry> Entries,
    IReadOnlyList<RecoveryDraftDeletion>? Deleted = null, IReadOnlyList<RecoveryDraftApproval>? Approvals = null)
{
    /// <summary>Number of individual changes the draft holds.</summary>
    public int ChangeCount => Entries.Count + (Deleted?.Count ?? 0) + (Approvals?.Count ?? 0);
}

/// <summary>What re-applying a draft did. Entries whose on-disk value changed since the draft are never applied.</summary>
public sealed record RecoveryApplyResult(int Applied, IReadOnlyList<RecoveryDraftEntry> Conflicts);

/// <summary>Persists and re-applies recovery drafts.</summary>
public interface IRecoveryDraftService
{
    /// <summary>Writes (or replaces) the draft for a project.</summary>
    void Write(string projectPath, RecoveryDraft draft);

    /// <summary>The draft for a project, or null when none exists or it cannot be read.</summary>
    RecoveryDraft? TryRead(string projectPath);

    /// <summary>Removes the draft for a project.</summary>
    void Delete(string projectPath);
}
