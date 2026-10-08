using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Orchestrates Open, Close, Save, and Save As flows for translation projects.
/// All project-opening paths funnel through this service for consistent behavior.
/// </summary>
public partial class ProjectLifecycleService(
    IProjectService projectService,
    ITranslationManagementService translationManagement,
    IFileWatcherService fileWatcher,
    IValidationPipeline validationPipeline,
    IAutoSaveService autoSave,
    IDiffMergeEngine diffMerge,
    IAuditService auditService,
    IRecentProjectService recentProjects,
    ICommentPersistenceService commentPersistence,
    LanguageManagementService languageManagement,
    IUnsavedChangesHandler? unsavedChangesHandler,
    IExternalChangeHandler? externalChangeHandler,
    ILogger<ProjectLifecycleService> logger,
    IRecoveryDraftService? recoveryDrafts = null) : IProjectLifecycleService
{
    private ProjectSettings? _currentProject;

    /// <inheritdoc />
    public bool IsProjectOpen => _currentProject != null;

    /// <inheritdoc />
    public ProjectSettings? CurrentProject => _currentProject;

    /// <inheritdoc />
    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;

    /// <inheritdoc />
    public async Task<ProjectOpenResult> OpenProjectAsync(string folderPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        // 1. If a project is already open and dirty, attempt to close it first
        if (IsProjectOpen && translationManagement.IsDirty)
        {
            var closeResult = await CloseProjectAsync(ct).ConfigureAwait(false);
            if (closeResult == CloseResult.Cancelled)
                return new ProjectOpenResult(ProjectOpenStatus.Cancelled);
        }
        else if (IsProjectOpen)
        {
            // Project is open but not dirty — just clean up
            CleanupCurrentProject();
        }

        // 2. Validate folder exists
        if (!Directory.Exists(folderPath))
        {
            logger.LogWarning("Project folder not found: {FolderPath}", folderPath);
            recentProjects.Remove(folderPath);
            recentProjects.Save();
            return new ProjectOpenResult(ProjectOpenStatus.FolderNotFound, $"Folder not found: {folderPath}");
        }

        // 3. Try to load via IProjectService
        ProjectLoadResult loadResult;
        try
        {
            // Off the UI thread: a large folder can take a long time and must stay cancellable.
            // No ConfigureAwait(false): the steps below and ProjectChanged handlers expect the caller's (UI) context.
            loadResult = await Task.Run(() => projectService.LoadProject(folderPath, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Project open cancelled while scanning: {FolderPath}", folderPath);
            return new ProjectOpenResult(ProjectOpenStatus.Cancelled);
        }
        catch (FormatUnavailableException ex)
        {
            logger.LogWarning(ex, "Project format unavailable in: {FolderPath}", folderPath);
            return new ProjectOpenResult(ProjectOpenStatus.FormatUnavailable, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or IOException)
        {
            logger.LogError(ex, "Failed to parse project manifest in: {FolderPath}", folderPath);
            return new ProjectOpenResult(ProjectOpenStatus.ManifestInvalid, $"Invalid project manifest: {ex.Message}");
        }

        var settings = loadResult.Settings;
        var translations = loadResult.Translations;

        // 4. Initialize ITranslationManagementService with loaded translations
        translationManagement.Initialize(translations);

        // 5. Start IFileWatcherService on the folder
        fileWatcher.Watch(folderPath);
        SubscribeToFileWatcher();
        _externalChangeHandler = externalChangeHandler;

        // 6. Add to recent projects
        recentProjects.Add(folderPath);
        recentProjects.Save();

        // 7. Load audit metadata
        try
        {
            auditService.LoadFromSidecar(folderPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load audit metadata from sidecar for: {FolderPath}", folderPath);
        }

        // 8. Load comments via ICommentPersistenceService
        try
        {
            commentPersistence.LoadComments(folderPath, settings.SaveFormat, translations);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load comments for: {FolderPath}", folderPath);
        }

        // 9. Set current project and configure language management
        _currentProject = settings;
        languageManagement.SetProjectSettings(settings);

        // 10. Update last-saved snapshot for three-way merge baseline
        UpdateLastSavedSnapshot();

        // 11. Start auto-save if enabled in effective settings
        if (settings.AutoSaveEnabled == true)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(settings.AutoSaveIntervalSeconds ?? 60, 10, 600));
            autoSave.Start(interval);
        }

        // 11b. Leftovers from an earlier session: an unsaved-edits draft, or a save that never finished
        DetectLeftovers(folderPath);
        StartDraftTimer();

        // 12. Raise ProjectChanged event with Opened
        ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
        {
            ProjectPath = folderPath,
            ChangeType = ProjectChangeType.Opened
        });

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Project opened successfully: {FolderPath}", folderPath);
        return new ProjectOpenResult(ProjectOpenStatus.Success, Warnings: loadResult.Warnings,
            RecoveryDraft: _pendingRecovery, InterruptedSave: HasInterruptedSave);
    }

    /// <inheritdoc />
    public async Task<ProjectOpenResult> CreateAndOpenProjectAsync(string folder, IReadOnlyList<string> languages, string formatId, string? name = null, CancellationToken ct = default)
    {
        // 1. If a project is already open and dirty, attempt to close it first
        if (IsProjectOpen && translationManagement.IsDirty)
        {
            var closeResult = await CloseProjectAsync(ct).ConfigureAwait(false);
            if (closeResult == CloseResult.Cancelled)
                return new ProjectOpenResult(ProjectOpenStatus.Cancelled);
        }
        else if (IsProjectOpen)
        {
            CleanupCurrentProject();
        }

        // 2. Create the project folder, manifest, and language files
        projectService.CreateProject(folder, languages, formatId, createManifest: true, name);

        // 3. Open through the unified load pipeline
        return await OpenProjectAsync(folder, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProjectSaveResult> SaveProjectAsync(CancellationToken ct = default) => SaveProjectAsync(new SaveOptions(), ct);

    /// <inheritdoc />
    public Task<ProjectSaveResult> SaveProjectAsync(SaveOptions options, CancellationToken ct = default)
    {
        if (!IsProjectOpen || _currentProject is null)
            return Task.FromResult(new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: "No project is currently open."));

        SaveTransaction? transaction = null;
        try
        {
            // 1. Validate. Findings never stop a draft from being saved; only strict policy (approval, delivery) does.
            var findings = validationPipeline.RunAll(new ValidationContext
            {
                Items = translationManagement.Translations,
                PrimaryLanguage = _currentProject.PrimaryLanguage
            }).ToList();

            var errors = findings.Where(r => r.Severity == ValidationSeverity.Error).ToList();
            if (options.EnforceValidation && errors.Count > 0)
                return Task.FromResult(new ProjectSaveResult(ProjectSaveStatus.ValidationErrors, Errors: errors, Findings: findings));

            // 2. Refuse to overwrite edits made outside Toucan unless the caller said so
            var files = TrackedFiles(_currentProject);
            if (!options.OverwriteExternalChanges)
            {
                var changed = ExternallyChangedFiles(_currentProject);
                if (changed.Count > 0)
                    return Task.FromResult(new ProjectSaveResult(ProjectSaveStatus.ExternalChanges,
                        ErrorMessage: $"{changed.Count} project file(s) changed outside Toucan: {string.Join(", ", changed.Select(Path.GetFileName))}. Nothing was saved.",
                        Findings: findings, ExternalFiles: changed));
            }

            // 3. Keep the latest edits in a draft, then snapshot every file the save may touch
            FlushRecoveryDraft();
            var projectPath = _currentProject.ProjectPath;
            transaction = SaveTransaction.Begin(projectPath, files);

            // 4. Persist translations, comments, audit data and the manifest as one unit
            projectService.Save(_currentProject, [], translationManagement.Translations);
            commentPersistence.SaveComments(projectPath, _currentProject.SaveFormat, translationManagement.Translations);
            auditService.SaveToSidecar(projectPath);
            UpdateManifestTranslationPackages(_currentProject);
            _currentProject.Save();
            transaction.Commit();

            // 5. Everything is on disk: only now do the unsaved edits count as saved
            translationManagement.MarkAllSaved();
            DeleteRecoveryDraft();
            fileWatcher.TakeSnapshot();
            UpdateLastSavedSnapshot();
            if (autoSave.IsEnabled)
                autoSave.ResetTimer();

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ProjectPath = projectPath,
                ChangeType = ProjectChangeType.Saved
            });

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Project saved successfully: {ProjectPath}", projectPath);

            return Task.FromResult(new ProjectSaveResult(ProjectSaveStatus.Success, Findings: findings));
        }
        catch (Exception ex)
        {
            if (ex is not (IOException or UnauthorizedAccessException))
            {
                transaction?.Rollback();
                throw;
            }

            logger.LogError(ex, "Save failed due to file system error");
            var rollback = transaction?.Rollback();
            var detail = rollback switch
            {
                null => "No files were changed.",
                { Complete: true } => "Your files were restored to their previous content.",
                _ => $"{rollback.Failed.Count} file(s) could not be restored ({string.Join(", ", rollback.Failed.Select(f => Path.GetFileName(f.File)))}); Toucan will offer to restore them the next time the project opens."
            };
            // The in-memory edits stay dirty: nothing was reported as saved.
            return Task.FromResult(new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: $"{ex.Message} {detail} Your unsaved changes are still open."));
        }
    }

    /// <inheritdoc />
    public async Task<ProjectSaveResult> SaveProjectAsAsync(string targetFolder, CancellationToken ct = default)
    {
        if (!IsProjectOpen || _currentProject is null)
            return new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: "No project is currently open.");

        try
        {
            // 1. Create target folder if needed
            Directory.CreateDirectory(targetFolder);

            // 2. Persist translations to new folder
            var newSettings = new ProjectSettings
            {
                Name = _currentProject.Name,
                Description = _currentProject.Description,
                Version = _currentProject.Version,
                PrimaryLanguage = _currentProject.PrimaryLanguage,
                Languages = [.. _currentProject.Languages],
                SaveFormat = _currentProject.SaveFormat,
                TextEncoding = _currentProject.TextEncoding,
                LineEnding = _currentProject.LineEnding,
                DefaultPathResolver = _currentProject.DefaultPathResolver,
                Framework = _currentProject.Framework,
                TranslationPackages = [.. _currentProject.TranslationPackages],
                SaveEmptyTranslations = _currentProject.SaveEmptyTranslations,
                TranslationOrder = _currentProject.TranslationOrder,
                CopyTemplates = _currentProject.CopyTemplates != null ? [.. _currentProject.CopyTemplates] : null,
                DefaultProvider = _currentProject.DefaultProvider,
                LanguageAliases = _currentProject.LanguageAliases != null
                    ? new Dictionary<string, string>(_currentProject.LanguageAliases)
                    : null,
                LanguageFilePaths = _currentProject.LanguageFilePaths != null
                    ? new Dictionary<string, string>(_currentProject.LanguageFilePaths)
                    : null,
                SourceRoots = _currentProject.SourceRoots != null ? [.. _currentProject.SourceRoots] : null,
                ExternalEditor = _currentProject.ExternalEditor,
                AutoSaveEnabled = _currentProject.AutoSaveEnabled,
                AutoSaveIntervalSeconds = _currentProject.AutoSaveIntervalSeconds,
                ProjectPath = targetFolder
            };

            projectService.Save(newSettings, [], translationManagement.Translations);

            // 3. Save comments and audit sidecar to new folder
            commentPersistence.SaveComments(targetFolder, newSettings.SaveFormat, translationManagement.Translations);
            auditService.SaveToSidecar(targetFolder);

            // 4. Update manifest translationPackages and save to new folder
            UpdateManifestTranslationPackages(newSettings);
            newSettings.Save();

            // 5. Stop old watcher, start new watcher on new folder
            UnsubscribeFromFileWatcher();
            fileWatcher.Stop();
            fileWatcher.Watch(targetFolder);
            SubscribeToFileWatcher();

            // 6. Update project path in current settings
            _currentProject = newSettings;
            languageManagement.SetProjectSettings(newSettings);

            // 7. Update recent projects
            recentProjects.Add(targetFolder);
            recentProjects.Save();

            // 8. Mark all translations as saved
            translationManagement.MarkAllSaved();

            // 9. Update last-saved snapshot for merge baseline
            UpdateLastSavedSnapshot();

            // 10. Reset auto-save timer if enabled
            if (autoSave.IsEnabled)
                autoSave.ResetTimer();

            // 11. Raise ProjectChanged with Saved
            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ProjectPath = targetFolder,
                ChangeType = ProjectChangeType.Saved
            });

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Project saved as to new folder: {TargetFolder}", targetFolder);

            return new ProjectSaveResult(ProjectSaveStatus.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Save As failed due to file system error: {TargetFolder}", targetFolder);
            return new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Updates the translationPackages entries in the manifest so each package's translationUrls
    /// contains one entry per language with its relative file path.
    /// </summary>
    private void UpdateManifestTranslationPackages(ProjectSettings settings)
    {
        // Ensure at least one package exists
        if (settings.TranslationPackages.Count == 0)
            settings.TranslationPackages.Add(new TranslationPackage { Name = "main" });

        var package = settings.TranslationPackages[0];
        package.TranslationUrls = settings.Languages
            .Select(lang => new TranslationUrl
            {
                Language = lang,
                Path = projectService.GetDefaultFilePath(settings, lang)
            })
            .ToList();
    }


    /// <inheritdoc />
    public async Task<CloseResult> CloseProjectAsync(CancellationToken ct = default)
    {
        if (!IsProjectOpen)
            return CloseResult.Closed;

        // If project has unsaved changes, prompt the user
        if (translationManagement.IsDirty)
        {
            // Determine the user's choice — default to Save if no handler is set
            var choice = UnsavedChangesChoice.Save;
            if (_unsavedChangesHandler != null)
            {
                choice = await _unsavedChangesHandler.PromptAsync().ConfigureAwait(false);
            }

            switch (choice)
            {
                case UnsavedChangesChoice.Save:
                    var saveResult = await SaveProjectAsync(ct).ConfigureAwait(false);
                    if (saveResult.Status != ProjectSaveStatus.Success)
                    {
                        // Save failed — keep the project open so the user doesn't lose data
                        logger.LogWarning("Close cancelled: save failed with status {Status}", saveResult.Status);
                        return CloseResult.Cancelled;
                    }
                    break;

                case UnsavedChangesChoice.Discard:
                    // The user chose to throw the edits away, so the recovery draft must not bring them back
                    DeleteRecoveryDraft();
                    break;

                case UnsavedChangesChoice.Cancel:
                    return CloseResult.Cancelled;
            }
        }

        // Raise Closed event before cleanup (while ProjectPath is still available)
        var projectPath = _currentProject!.ProjectPath;
        ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
        {
            ProjectPath = projectPath,
            ChangeType = ProjectChangeType.Closed
        });

        // Cleanup all project state
        CleanupCurrentProject();

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Project closed: {ProjectPath}", projectPath);

        return CloseResult.Closed;
    }

    /// <summary>
    /// Cleans up the current project state without prompting for unsaved changes.
    /// Used internally when switching projects where the current one is not dirty.
    /// </summary>
    private void CleanupCurrentProject()
    {
        autoSave.Stop();
        StopDraftTimer();
        _pendingRecovery = null;
        _interruptedSave = null;
        _diskBaseline = [];
        UnsubscribeFromFileWatcher();
        fileWatcher.Stop();
        translationManagement.Clear();
        auditService.Clear();
        languageManagement.SetProjectSettings(null);
        _lastSavedSnapshot = [];
        _currentProject = null;
    }
}
