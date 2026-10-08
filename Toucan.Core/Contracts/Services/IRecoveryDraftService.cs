using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>One unsaved edit. <see cref="BaseValue"/> is the value on disk when the edit started; null means a new entry.</summary>
public sealed record RecoveryDraftEntry(string Language, string Namespace, string? BaseValue, string Value, string? BaseComment, string? Comment);

/// <summary>Unsaved edits of one project, kept outside the project folder so a crash does not lose them.</summary>
public sealed record RecoveryDraft(string ProjectPath, DateTime SavedAtUtc, IReadOnlyList<RecoveryDraftEntry> Entries);

/// <summary>What re-applying a draft did. Entries whose on-disk value changed since the draft are never applied.</summary>
public sealed record RecoveryApplyResult(int Applied, IReadOnlyList<RecoveryDraftEntry> Conflicts);

/// <summary>Persists and re-applies recovery drafts.</summary>
public interface IRecoveryDraftService
{
    /// <summary>Writes (or replaces) the draft for a project.</summary>
    void Write(string projectPath, IReadOnlyList<RecoveryDraftEntry> entries);

    /// <summary>The draft for a project, or null when none exists or it cannot be read.</summary>
    RecoveryDraft? TryRead(string projectPath);

    /// <summary>Removes the draft for a project.</summary>
    void Delete(string projectPath);
}
