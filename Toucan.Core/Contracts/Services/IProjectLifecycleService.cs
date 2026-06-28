using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Orchestrates Open, Close, Save, and Save As flows for translation projects.
/// All project-opening paths funnel through this service for consistent behavior.
/// </summary>
public interface IProjectLifecycleService
{
    /// <summary>Opens a project from any entry point. Handles unsaved-changes prompt if needed.</summary>
    Task<ProjectOpenResult> OpenProjectAsync(string folderPath, CancellationToken ct = default);

    /// <summary>Creates a new project and opens it.</summary>
    Task<ProjectOpenResult> CreateAndOpenProjectAsync(string folder, IReadOnlyList<string> languages, SaveStyles style, string? name = null, CancellationToken ct = default);

    /// <summary>Saves the current project in place.</summary>
    Task<ProjectSaveResult> SaveProjectAsync(CancellationToken ct = default);

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
    Cancelled
}

/// <summary>Result of a project open operation.</summary>
public record ProjectOpenResult(ProjectOpenStatus Status, string? ErrorMessage = null);

/// <summary>Status codes for project save operations.</summary>
public enum ProjectSaveStatus
{
    Success,
    ValidationErrors,
    FileSystemError,
    Cancelled
}

/// <summary>Result of a project save operation.</summary>
public record ProjectSaveResult(ProjectSaveStatus Status, IReadOnlyList<ValidationResult>? Errors = null, string? ErrorMessage = null);

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
