using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Partial class handling external file change detection and merge logic.
/// Subscribes to IFileWatcherService.FilesChanged and coordinates reload/merge/ignore flows.
/// </summary>
public partial class ProjectLifecycleService
{
    private IExternalChangeHandler? _externalChangeHandler;

    /// <summary>
    /// Stores the last-saved snapshot of translations for three-way merge (base).
    /// Updated after each successful load or save.
    /// </summary>
    private IReadOnlyList<TranslationItem> _lastSavedSnapshot = [];

    private IUnsavedChangesHandler? _unsavedChangesHandler = unsavedChangesHandler;

    /// <summary>
    /// Sets the unsaved changes handler. Called during service configuration
    /// to allow UI-layer implementations to be wired after construction.
    /// </summary>
    public void SetUnsavedChangesHandler(IUnsavedChangesHandler? handler)
    {
        _unsavedChangesHandler = handler;
    }

    /// <summary>
    /// Sets the external change handler. Called during service configuration
    /// to allow UI-layer implementations to be wired after construction.
    /// </summary>
    public void SetExternalChangeHandler(IExternalChangeHandler? handler)
    {
        _externalChangeHandler = handler;
    }

    /// <summary>
    /// Subscribes to the file watcher's FilesChanged event.
    /// Called after starting the watcher in OpenProjectAsync.
    /// </summary>
    private void SubscribeToFileWatcher()
    {
        fileWatcher.FilesChanged += OnExternalFilesChanged;
    }

    /// <summary>
    /// Unsubscribes from the file watcher's FilesChanged event.
    /// Called during project cleanup.
    /// </summary>
    private void UnsubscribeFromFileWatcher()
    {
        fileWatcher.FilesChanged -= OnExternalFilesChanged;
    }

    /// <summary>
    /// Updates the last-saved snapshot with a copy of the current translations.
    /// Called after loading or saving the project.
    /// </summary>
    private void UpdateLastSavedSnapshot()
    {
        _lastSavedSnapshot = translationManagement.Translations
            .Select(t => new TranslationItem
            {
                Language = t.Language,
                Namespace = t.Namespace,
                Value = t.Value,
                Comment = t.Comment,
                IsApproved = t.IsApproved
            })
            .ToList();
    }

    /// <summary>
    /// Handles the FilesChanged event from IFileWatcherService.
    /// If not dirty: auto-reload. If dirty: prompt user.
    /// </summary>
    private async void OnExternalFilesChanged(object? sender, EventArgs e)
    {
        if (!IsProjectOpen || _currentProject is null)
            return;

        try
        {
            if (!translationManagement.IsDirty)
            {
                // Not dirty: auto-reload affected files, update snapshot
                await ReloadFromDiskAsync().ConfigureAwait(false);
            }
            else
            {
                // Dirty: prompt user with Reload/Merge/Ignore options
                if (_externalChangeHandler is null)
                {
                    // No handler available — ignore by default (e.g., in tests)
                    logger.LogDebug("External file changes detected but no handler available; ignoring.");
                    return;
                }

                var choice = await _externalChangeHandler.PromptAsync().ConfigureAwait(false);

                switch (choice)
                {
                    case ExternalChangeChoice.Reload:
                        await ReloadFromDiskAsync().ConfigureAwait(false);
                        break;

                    case ExternalChangeChoice.Merge:
                        await MergeExternalChangesAsync().ConfigureAwait(false);
                        break;

                    case ExternalChangeChoice.Ignore:
                        // Do nothing — retain in-memory state
                        logger.LogDebug("User chose to ignore external file changes.");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling external file changes");
        }
    }

    /// <summary>
    /// Reloads all translations from disk, clears dirty state, and updates the snapshot.
    /// Used for both auto-reload (not dirty) and explicit Reload choice.
    /// </summary>
    private async Task ReloadFromDiskAsync()
    {
        if (_currentProject is null)
            return;

        var folderPath = _currentProject.ProjectPath;

        try
        {
            // Reload translations from disk
            var loadResult = projectService.LoadProject(folderPath);
            var translations = loadResult.Translations;

            // Re-initialize the translation management service (clears dirty and sets new baselines)
            translationManagement.Initialize(translations);

            // Reload comments
            try
            {
                commentPersistence.LoadComments(folderPath, _currentProject.SaveStyle, translations);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reload comments after external change");
            }

            // Update the file watcher snapshot
            fileWatcher.TakeSnapshot();

            // Update the last-saved snapshot
            UpdateLastSavedSnapshot();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Project reloaded from disk after external changes: {FolderPath}", folderPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reload project from disk: {FolderPath}", folderPath);
        }
    }

    /// <summary>
    /// Performs a three-way merge between base (last save), mine (in-memory), and theirs (disk).
    /// Applies non-conflicting changes automatically, then presents conflicts to the user.
    /// </summary>
    private async Task MergeExternalChangesAsync()
    {
        if (_currentProject is null)
            return;

        var folderPath = _currentProject.ProjectPath;

        try
        {
            // Load "theirs" from disk
            var loadResult = projectService.LoadProject(folderPath);
            var theirs = loadResult.Translations;

            // "base" is the last-saved snapshot
            var baseSnapshot = _lastSavedSnapshot;

            // "mine" is the current in-memory state
            var mine = translationManagement.Translations.ToList();

            // Compute three-way diff
            var diffResult = diffMerge.ComputeDiff(baseSnapshot, mine, theirs);

            // Apply non-conflicting changes automatically
            var mergeResult = diffMerge.ApplyNonConflicting(diffResult, translationManagement);

            if (mergeResult.Conflicts.Count > 0 && _externalChangeHandler is not null)
            {
                // Present conflicts to the user for resolution
                var resolved = await _externalChangeHandler
                    .ShowConflictResolutionAsync(mergeResult.Conflicts)
                    .ConfigureAwait(false);

                if (resolved is not null)
                {
                    // Apply resolved values to in-memory state
                    ApplyResolvedConflicts(resolved);
                }
                else
                {
                    // User cancelled conflict resolution — non-conflicting changes already applied
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation("User cancelled conflict resolution; non-conflicting changes were already applied.");
                }
            }

            // Update the file watcher snapshot (we've acknowledged disk changes)
            fileWatcher.TakeSnapshot();

            // Update the last-saved snapshot to reflect the new disk state
            UpdateLastSavedSnapshot();

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(
                    "Merge completed: {AutoApplied} auto-applied, {Conflicts} conflicts",
                    mergeResult.AutoApplied,
                    mergeResult.Conflicts.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to merge external changes: {FolderPath}", folderPath);
        }
    }

    /// <summary>
    /// Applies user-resolved conflict entries to the in-memory translation collection.
    /// Each resolved entry updates or adds the item with the user's chosen value.
    /// </summary>
    private void ApplyResolvedConflicts(IReadOnlyList<DiffEntry> resolvedEntries)
    {
        foreach (var entry in resolvedEntries)
        {
            // Find the matching in-memory item by composite key
            var item = translationManagement.Translations
                .FirstOrDefault(t => t.Language == entry.Language && t.Namespace == entry.Namespace);

            if (item is not null && entry.TheirsValue is not null)
            {
                // The resolved value is stored in TheirsValue for "accept theirs" or MineValue for "keep mine"
                // The conflict resolution dialog returns the chosen value in TheirsValue
                translationManagement.NotifyValueChanged(item, entry.TheirsValue);
            }
            else if (item is null && entry.TheirsValue is not null)
            {
                // Item was added on disk and resolved — add it
                translationManagement.AddItems([new TranslationItem
                {
                    Language = entry.Language,
                    Namespace = entry.Namespace,
                    Value = entry.TheirsValue
                }]);
            }
        }
    }
}
