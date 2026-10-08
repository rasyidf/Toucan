using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Orchestrates Open, Close, Save, and Save As flows for translation projects.
/// All project-opening paths funnel through this service for consistent behavior.
/// </summary>
public interface IProjectLifecycleService
{
    /// <summary>Opens a project from any entry point. Handles unsaved-changes prompt if needed. Cancelling <paramref name="ct"/> mid-scan returns <see cref="ProjectOpenStatus.Cancelled"/>.</summary>
    Task<ProjectOpenResult> OpenProjectAsync(string folderPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Creates a new project and opens it.</summary>
    Task<ProjectOpenResult> CreateAndOpenProjectAsync(string folder, IReadOnlyList<string> languages, string formatId, string? name = null, CancellationToken ct = default);

    /// <summary>
    /// Saves the current project in place. Validation findings never block the save (drafts are saveable); they are
    /// returned in <see cref="ProjectSaveResult.Findings"/>. Fails with <see cref="ProjectSaveStatus.ExternalChanges"/>
    /// when project files changed on disk since they were loaded or last saved.
    /// </summary>
    Task<ProjectSaveResult> SaveProjectAsync(CancellationToken ct = default);

    /// <summary>Saves the current project in place with explicit policy (strict validation, overwrite of external edits).</summary>
    Task<ProjectSaveResult> SaveProjectAsync(SaveOptions options, CancellationToken ct = default);

    /// <summary>Unsaved edits found for the project that was just opened, awaiting <see cref="ApplyRecovery"/> or <see cref="DiscardRecovery"/>.</summary>
    RecoveryDraft? PendingRecovery { get; }

    /// <summary>A save that was interrupted (crash or failed restore) and left files in a mixed state, awaiting a decision.</summary>
    bool HasInterruptedSave { get; }

    /// <summary>Re-applies the pending recovery draft. Entries whose on-disk value changed since the draft are skipped and reported.</summary>
    RecoveryApplyResult ApplyRecovery();

    /// <summary>Throws away the pending recovery draft.</summary>
    void DiscardRecovery();

    /// <summary>Restores the files an interrupted save touched to their pre-save content and reloads the project.</summary>
    Task<SaveRollbackResult> RestoreInterruptedSaveAsync();

    /// <summary>Keeps what is on disk after an interrupted save and removes the journal.</summary>
    void KeepInterruptedSave();

    /// <summary>Saves the current project to a new folder.</summary>
    Task<ProjectSaveResult> SaveProjectAsAsync(string targetFolder, CancellationToken ct = default);

    /// <summary>Closes the current project. Shows unsaved prompt if dirty.</summary>
    Task<CloseResult> CloseProjectAsync(CancellationToken ct = default);

    /// <summary>Whether a project is currently loaded.</summary>
    bool IsProjectOpen { get; }

    /// <summary>Current project settings (null when no project open).</summary>
    ProjectSettings? CurrentProject { get; }

    /// <summary>Raised when the project changes (open/close/save).</summary>
    event EventHandler<ProjectChangedEventArgs>? ProjectChanged;
}

/// <summary>Status codes for project open operations.</summary>
public enum ProjectOpenStatus
{
    Success,
    FolderNotFound,
    ManifestInvalid,
    /// <summary>The project's format (e.g. from a plugin) is not installed or enabled.</summary>
    FormatUnavailable,
    Cancelled
}

/// <summary>Result of a project open operation.</summary>
/// <param name="Warnings">Format limitations to show after a successful open.</param>
/// <param name="RecoveryDraft">Unsaved edits from an earlier session; the caller should offer to recover them.</param>
/// <param name="InterruptedSave">True when an earlier save was interrupted and left files in a mixed state.</param>
public record ProjectOpenResult(ProjectOpenStatus Status, string? ErrorMessage = null, IReadOnlyList<string>? Warnings = null,
    RecoveryDraft? RecoveryDraft = null, bool InterruptedSave = false);

/// <summary>Status codes for project save operations.</summary>
public enum ProjectSaveStatus
{
    Success,
    /// <summary>Only returned when <see cref="SaveOptions.EnforceValidation"/> is set.</summary>
    ValidationErrors,
    FileSystemError,
    /// <summary>Project files were modified outside Toucan; nothing was written.</summary>
    ExternalChanges,
    Cancelled
}

/// <summary>Result of a project save operation.</summary>
/// <param name="Errors">Blocking errors (strict validation only).</param>
/// <param name="Findings">Validation findings on the data that was saved; the save went ahead regardless.</param>
/// <param name="ExternalFiles">Files that changed on disk (<see cref="ProjectSaveStatus.ExternalChanges"/>).</param>
public record ProjectSaveResult(ProjectSaveStatus Status, IReadOnlyList<ValidationResult>? Errors = null, string? ErrorMessage = null,
    IReadOnlyList<ValidationResult>? Findings = null, IReadOnlyList<string>? ExternalFiles = null);

/// <summary>Policy for a save.</summary>
/// <param name="EnforceValidation">Refuse to save while validation errors exist. Used for approval and delivery, not for drafts.</param>
/// <param name="OverwriteExternalChanges">Write even though project files changed on disk since they were loaded.</param>
public sealed record SaveOptions(bool EnforceValidation = false, bool OverwriteExternalChanges = false);

/// <summary>Result of a project close operation.</summary>
public enum CloseResult
{
    Closed,
    Cancelled
}

/// <summary>Event args raised when the project state changes.</summary>
public class ProjectChangedEventArgs : EventArgs
{
    /// <summary>The path to the project folder.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>The type of change that occurred.</summary>
    public required ProjectChangeType ChangeType { get; init; }
}

/// <summary>Types of project state changes.</summary>
public enum ProjectChangeType
{
    Opened,
    Closed,
    Saved
}
