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
    ILogger<ProjectLifecycleService> logger) : IProjectLifecycleService
{
    private ProjectSettings? _currentProject;

    /// <inheritdoc />
    public bool IsProjectOpen => _currentProject != null;

    /// <inheritdoc />
    public ProjectSettings? CurrentProject => _currentProject;

    /// <inheritdoc />
    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;

    /// <inheritdoc />
    public async Task<ProjectOpenResult> OpenProjectAsync(string folderPath, CancellationToken ct = default)
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
            loadResult = projectService.LoadProject(folderPath);
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
            commentPersistence.LoadComments(folderPath, settings.SaveStyle, translations);
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

        // 11. Start auto-save if enabled in settings
        if (settings.AutoSaveEnabled)
        {
            var interval = TimeSpan.FromSeconds(Math.Clamp(settings.AutoSaveIntervalSeconds, 10, 600));
            autoSave.Start(interval);
        }

        // 12. Raise ProjectChanged event with Opened
        ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
        {
            ProjectPath = folderPath,
            ChangeType = ProjectChangeType.Opened
        });

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Project opened successfully: {FolderPath}", folderPath);
        return new ProjectOpenResult(ProjectOpenStatus.Success);
    }

    /// <inheritdoc />
    public async Task<ProjectOpenResult> CreateAndOpenProjectAsync(string folder, IReadOnlyList<string> languages, SaveStyles style, string? name = null, CancellationToken ct = default)
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
        projectService.CreateProject(folder, languages, style, createManifest: true, name);

        // 3. Open through the unified load pipeline
        return await OpenProjectAsync(folder, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProjectSaveResult> SaveProjectAsync(CancellationToken ct = default)
    {
        if (!IsProjectOpen || _currentProject is null)
            return new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: "No project is currently open.");

        try
        {
            // 1. Run validation pipeline
            var validationResults = validationPipeline.RunAll(new ValidationContext
            {
                Items = translationManagement.Translations,
                Settings = _currentProject
            }).ToList();

            var errors = validationResults.Where(r => r.Severity == ValidationSeverity.Error).ToList();
            if (errors.Count > 0)
                return new ProjectSaveResult(ProjectSaveStatus.ValidationErrors, Errors: errors);

            // 2. Persist translations via IProjectService
            var projectPath = _currentProject.ProjectPath;
            projectService.Save(_currentProject, [], translationManagement.Translations);

            // 3. Save comments via ICommentPersistenceService
            commentPersistence.SaveComments(projectPath, _currentProject.SaveStyle, translationManagement.Translations);

            // 4. Save audit sidecar
            auditService.SaveToSidecar(projectPath);

            // 5. Update manifest translationPackages and persist
            UpdateManifestTranslationPackages(_currentProject);
            _currentProject.Save();

            // 6. Mark all translations as saved
            translationManagement.MarkAllSaved();

            // 7. Update file watcher snapshot
            fileWatcher.TakeSnapshot();

            // 8. Update last-saved snapshot for merge baseline
            UpdateLastSavedSnapshot();

            // 9. Reset auto-save timer
            if (autoSave.IsEnabled)
                autoSave.ResetTimer();

            // 10. Raise ProjectChanged with Saved
            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs
            {
                ProjectPath = projectPath,
                ChangeType = ProjectChangeType.Saved
            });

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Project saved successfully: {ProjectPath}", projectPath);

            return new ProjectSaveResult(ProjectSaveStatus.Success);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Save failed due to file system error");
            return new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: ex.Message);
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
                SaveStyle = _currentProject.SaveStyle,
                Framework = _currentProject.Framework,
                TranslationPackages = [.. _currentProject.TranslationPackages],
                SaveEmptyTranslations = _currentProject.SaveEmptyTranslations,
                TranslationOrder = _currentProject.TranslationOrder,
                CopyTemplates = [.. _currentProject.CopyTemplates],
                DefaultProvider = _currentProject.DefaultProvider,
                LanguageAliases = _currentProject.LanguageAliases != null
                    ? new Dictionary<string, string>(_currentProject.LanguageAliases)
                    : null,
                LanguageFilePaths = _currentProject.LanguageFilePaths != null
                    ? new Dictionary<string, string>(_currentProject.LanguageFilePaths)
                    : null,
                SourceRoots = [.. _currentProject.SourceRoots],
                ExternalEditor = _currentProject.ExternalEditor,
                AutoSaveEnabled = _currentProject.AutoSaveEnabled,
                AutoSaveIntervalSeconds = _currentProject.AutoSaveIntervalSeconds,
                ProjectPath = targetFolder
            };

            projectService.Save(newSettings, [], translationManagement.Translations);

            // 3. Save comments and audit sidecar to new folder
            commentPersistence.SaveComments(targetFolder, newSettings.SaveStyle, translationManagement.Translations);
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
        catch (IOException ex)
        {
            logger.LogError(ex, "Save As failed due to file system error: {TargetFolder}", targetFolder);
            return new ProjectSaveResult(ProjectSaveStatus.FileSystemError, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Updates the translationPackages entries in the manifest so each package's translationUrls
    /// contains one entry per language with its relative file path.
    /// </summary>
    private static void UpdateManifestTranslationPackages(ProjectSettings settings)
    {
        // Ensure at least one package exists
        if (settings.TranslationPackages.Count == 0)
            settings.TranslationPackages.Add(new TranslationPackage { Name = "main" });

        var package = settings.TranslationPackages[0];
        package.TranslationUrls = settings.Languages
            .Select(lang => new TranslationUrl
            {
                Language = lang,
                Path = ResolveRelativeTranslationPath(settings, lang)
            })
            .ToList();
    }

    /// <summary>
    /// Resolves the relative file path for a language based on the project's save style.
    /// </summary>
    private static string ResolveRelativeTranslationPath(ProjectSettings settings, string language)
    {
        // Check for custom file path override
        if (settings.LanguageFilePaths?.TryGetValue(language, out var customPath) == true)
            return customPath;

        return settings.SaveStyle switch
        {
            SaveStyles.Json => $"{language}.json",
            SaveStyles.Namespaced => $"{language}.json",
            SaveStyles.Yaml => $"{language}.yaml",
            SaveStyles.Toml => $"{language}.toml",
            SaveStyles.Properties => $"{language}.po",
            SaveStyles.Adb => $"{language}.ini",
            SaveStyles.AndroidXml => $"res/{(language == "default" ? "values" : $"values-{language}")}/strings.xml",
            SaveStyles.IosStrings => $"{language}.lproj/Localizable.strings",
            SaveStyles.Xliff => $"{language}.xlf",
            SaveStyles.Arb => $"app_{language}.arb",
            SaveStyles.Csv => "translations.csv",
            SaveStyles.Resx => $"Resources{(language == "default" ? "" : $".{language}")}.resx",
            SaveStyles.JavaProperties => $"{language}.properties",
            SaveStyles.LaravelPhp => $"{language}/messages.php",
            _ => $"{language}.json"
        };
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
            if (unsavedChangesHandler != null)
            {
                choice = await unsavedChangesHandler.PromptAsync().ConfigureAwait(false);
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
                    // Nothing to persist — just fall through to cleanup
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
        UnsubscribeFromFileWatcher();
        fileWatcher.Stop();
        translationManagement.Clear();
        auditService.Clear();
        languageManagement.SetProjectSettings(null);
        _lastSavedSnapshot = [];
        _currentProject = null;
    }
}
