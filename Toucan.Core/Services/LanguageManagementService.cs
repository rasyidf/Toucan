using System.IO;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services;

/// <summary>
/// Manages language lifecycle operations: add, remove, reorder, and file path resolution.
/// </summary>
public class LanguageManagementService(
    ITranslationManagementService translationManagement,
    IProjectService projectService,
    IFileWatcherService fileWatcher,
    ILogger<LanguageManagementService> logger) : ILanguageManagementService
{
    private ProjectSettings? _projectSettings;

    /// <summary>
    /// Sets the current project settings reference. Called by the lifecycle service after project open.
    /// </summary>
    public void SetProjectSettings(ProjectSettings? settings) => _projectSettings = settings;

    public async Task<LanguageOperationResult> AddLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return new LanguageOperationResult(false, "Language code cannot be empty.");

        var settings = _projectSettings;
        if (settings == null)
            return new LanguageOperationResult(false, "No project is currently open.");

        if (settings.Languages.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
            return new LanguageOperationResult(false, $"Language '{languageCode}' already exists in the project.");

        try
        {
            // Get all distinct namespaces from existing translations
            var namespaces = translationManagement.Translations
                .ForParse()
                .Select(t => t.Namespace)
                .Distinct()
                .ToList();

            // Create TranslationItems with empty values for every existing namespace
            var newItems = namespaces.Select(ns => new TranslationItem
            {
                Language = languageCode,
                Namespace = ns,
                Value = string.Empty
            }).ToList();

            // If no namespaces exist, create a placeholder so the language is recognized
            if (newItems.Count == 0)
            {
                newItems.Add(new TranslationItem
                {
                    Language = languageCode,
                    Namespace = string.Empty,
                    Value = string.Empty
                });
            }

            // Write language file to disk
            await Task.Run(() => projectService.CreateLanguage(
                settings.ProjectPath, languageCode, settings.SaveStyle), ct).ConfigureAwait(false);

            // Add items to in-memory collection
            translationManagement.AddItems(newItems);

            // Update manifest languages list and persist
            if (!settings.Languages.Contains(languageCode))
                settings.Languages.Add(languageCode);

            settings.Save();

            // Update file watcher snapshot to avoid false change detection
            fileWatcher.TakeSnapshot();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Language added successfully: {LanguageCode}", languageCode);
            return new LanguageOperationResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (logger.IsEnabled(LogLevel.Error))
                logger.LogError(ex, "Failed to add language: {LanguageCode}", languageCode);
            return new LanguageOperationResult(false, $"Failed to create language file: {ex.Message}");
        }
    }

    public async Task<LanguageOperationResult> RemoveLanguageAsync(string languageCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return new LanguageOperationResult(false, "Language code cannot be empty.");

        var settings = _projectSettings;
        if (settings == null)
            return new LanguageOperationResult(false, "No project is currently open.");

        // Reject if attempting to remove primary language
        if (string.Equals(settings.PrimaryLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return new LanguageOperationResult(false, $"Cannot remove primary language '{languageCode}'.");

        // Get file paths before removal for disk cleanup
        var filePaths = GetLanguageFilePaths(languageCode);

        // Remove items from in-memory collection
        translationManagement.RemoveItems(item =>
            string.Equals(item.Language, languageCode, StringComparison.OrdinalIgnoreCase));

        // Delete files from disk (best-effort, continue on failure)
        var failedPaths = new List<string>();
        await Task.Run(() =>
        {
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (logger.IsEnabled(LogLevel.Warning))
                        logger.LogWarning(ex, "Failed to delete file during language removal: {FilePath}", filePath);
                    failedPaths.Add(filePath);
                }
            }

            // Try to clean up empty directories left behind
            CleanupEmptyDirectories(filePaths);
        }, ct).ConfigureAwait(false);

        // Remove from manifest languages list
        settings.Languages.RemoveAll(l => string.Equals(l, languageCode, StringComparison.OrdinalIgnoreCase));

        // Safety: if primary language was somehow removed, assign first remaining
        if (!settings.Languages.Contains(settings.PrimaryLanguage, StringComparer.OrdinalIgnoreCase)
            && settings.Languages.Count > 0)
        {
            settings.PrimaryLanguage = settings.Languages[0];
        }

        // Persist manifest
        settings.Save();

        // Update file watcher snapshot
        fileWatcher.TakeSnapshot();

        if (failedPaths.Count > 0)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning("Language removed from project but some files could not be deleted: {LanguageCode}, FailedCount={Count}",
                    languageCode, failedPaths.Count);
            return new LanguageOperationResult(true,
                $"Language removed but {failedPaths.Count} file(s) could not be deleted.",
                failedPaths);
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Language removed successfully: {LanguageCode}", languageCode);
        return new LanguageOperationResult(true);
    }

    public IReadOnlyList<string> GetLanguageFilePaths(string languageCode)
    {
        var settings = _projectSettings;
        if (settings == null || string.IsNullOrEmpty(settings.ProjectPath))
            return [];

        var projectPath = settings.ProjectPath;
        var paths = new List<string>();

        // Check for custom file path override
        if (settings.LanguageFilePaths?.TryGetValue(languageCode, out var customPath) == true)
        {
            var fullPath = Path.IsPathRooted(customPath)
                ? customPath
                : Path.Combine(projectPath, customPath);
            paths.Add(fullPath);
            return paths;
        }

        // Determine file paths based on save style
        switch (settings.SaveStyle)
        {
            case SaveStyles.Json:
            case SaveStyles.Namespaced:
                paths.Add(Path.Combine(projectPath, languageCode + ".json"));
                // Namespaced also writes to locales/{lang}/ directory
                if (settings.SaveStyle == SaveStyles.Namespaced)
                {
                    var localesDir = Path.Combine(projectPath, "locales", languageCode);
                    if (Directory.Exists(localesDir))
                    {
                        paths.AddRange(Directory.GetFiles(localesDir, "*.json"));
                    }
                }
                break;

            case SaveStyles.Yaml:
                paths.Add(Path.Combine(projectPath, languageCode + ".yaml"));
                break;

            case SaveStyles.Toml:
                paths.Add(Path.Combine(projectPath, languageCode + ".toml"));
                break;

            case SaveStyles.Properties: // PO/gettext
                paths.Add(Path.Combine(projectPath, languageCode + ".po"));
                break;

            case SaveStyles.Adb: // INI
                paths.Add(Path.Combine(projectPath, languageCode + ".ini"));
                break;

            case SaveStyles.AndroidXml:
                var dirName = languageCode == "default" ? "values" : $"values-{languageCode}";
                var xmlPath = Path.Combine(projectPath, "res", dirName, "strings.xml");
                paths.Add(xmlPath);
                break;

            case SaveStyles.IosStrings:
                var iosPath = Path.Combine(projectPath, $"{languageCode}.lproj", "Localizable.strings");
                paths.Add(iosPath);
                break;

            case SaveStyles.Xliff:
                paths.Add(Path.Combine(projectPath, $"{languageCode}.xlf"));
                break;

            case SaveStyles.Arb:
                paths.Add(Path.Combine(projectPath, $"app_{languageCode}.arb"));
                break;

            case SaveStyles.Csv:
                // CSV stores all languages in a single file; no per-language file to remove
                paths.Add(Path.Combine(projectPath, "translations.csv"));
                break;

            case SaveStyles.Resx:
                var suffix = languageCode == "default" ? "" : $".{languageCode}";
                paths.Add(Path.Combine(projectPath, $"Resources{suffix}.resx"));
                break;

            case SaveStyles.JavaProperties:
                paths.Add(Path.Combine(projectPath, languageCode + ".properties"));
                break;

            case SaveStyles.LaravelPhp:
                var phpDir = Path.Combine(projectPath, languageCode);
                if (Directory.Exists(phpDir))
                {
                    paths.AddRange(Directory.GetFiles(phpDir, "*.php"));
                }
                else
                {
                    // If directory doesn't exist yet, at least indicate the expected directory
                    paths.Add(phpDir);
                }
                break;

            default:
                // Fallback to JSON convention
                paths.Add(Path.Combine(projectPath, languageCode + ".json"));
                break;
        }

        return paths;
    }

    public async Task ReorderLanguagesAsync(IReadOnlyList<string> orderedLanguages, CancellationToken ct = default)
    {
        var settings = _projectSettings;
        if (settings == null)
            return;

        await Task.Run(() =>
        {
            settings.Languages = orderedLanguages.ToList();
            settings.Save();
        }, ct).ConfigureAwait(false);

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Languages reordered: [{Languages}]", string.Join(", ", orderedLanguages));
    }

    /// <summary>
    /// Attempts to remove empty directories that were left behind after language file deletion.
    /// This is best-effort and failures are silently ignored.
    /// </summary>
    private void CleanupEmptyDirectories(IReadOnlyList<string> deletedFilePaths)
    {
        var directories = deletedFilePaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        foreach (var dir in directories)
        {
            try
            {
                if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug(ex, "Could not remove empty directory: {Directory}", dir);
            }
        }
    }
}
