using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>File operations: new, open, save, close, recent projects, import/export.</summary>
public partial class MainWindowViewModel
{
    private static readonly FileFilter[] TranslationFileFilters =
    [
        new("Translation files", "*.json", "*.yml", "*.yaml", "*.toml", "*.xml", "*.strings", "*.xlf", "*.xliff", "*.arb", "*.csv", "*.resx", "*.po", "*.ini", "*.properties", "*.php"),
        new("All files", "*")
    ];

    /// <summary>Export formats offered to the user, mapped to the save strategy they use.</summary>
    /// <summary>Export targets: every registered save strategy, so plugin formats appear automatically.</summary>
    public IReadOnlyList<(string Label, string FormatId)> ExportFormats =>
        _strategyFactory.SaveStrategies.Select(st => (st.DisplayName, st.FormatId)).ToList();

    [ObservableProperty] private ObservableCollection<Project> recentProjects = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    partial void OnRecentProjectsChanged(ObservableCollection<Project> value) => OnPropertyChanged(nameof(HasRecentProjects));

    // ───────────────────────── New / Open ─────────────────────────

    [RelayCommand]
    private async Task NewFolder()
    {
        var vm = await _dialogService.ShowNewProjectAsync();
        if (vm == null) return;

        if (vm.OpenExistingPath != null)
        {
            await OpenProjectAsync(vm.OpenExistingPath);
            return;
        }

        try
        {
            vm.CreateProject();
            await OpenProjectAsync(vm.ProjectFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _messageService.ShowMessageAsync($"Failed to create project: {ex.Message}", "New Project");
        }
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        var selected = await _dialogService.SelectFolderAsync(HasProject ? CurrentPath : AppOptions.LastProjectPath, "Open Translation Folder");
        if (selected != null) await OpenProjectAsync(selected);
    }

    [RelayCommand]
    private async Task OpenProjectFile()
    {
        var selected = await _dialogService.SelectFileAsync(CurrentPath, "Open Toucan Project",
            [new FileFilter("Toucan project", "*.tproj", "*.project"), new FileFilter("All files", "*")]);
        if (selected == null) return;

        var directory = Path.GetDirectoryName(selected);
        if (directory == null || !Directory.Exists(directory))
        {
            await _messageService.ShowMessageAsync($"Folder not found: {directory}", "Open Project");
            return;
        }
        await OpenProjectAsync(directory);
    }

    [RelayCommand]
    private async Task ImportProject()
    {
        var vm = await _dialogService.ShowImportProjectAsync();
        if (vm?.IsValid == true) await OpenProjectAsync(vm.Folder);
    }

    [RelayCommand]
    internal async Task OpenRecent()
    {
        RefreshRecentProjects();
        var first = RecentProjects.FirstOrDefault(p => !string.Equals(p.Path, CurrentPath, StringComparison.Ordinal))
            ?? RecentProjects.FirstOrDefault();
        if (first == null)
        {
            await _messageService.ShowMessageAsync("No recent projects found.", "Open Recent");
            return;
        }
        await OpenProjectAsync(first.Path);
    }

    [RelayCommand]
    private async Task OpenRecentProject(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!Directory.Exists(path))
        {
            _recentFileService.Remove(path);
            RefreshRecentProjects();
            await _messageService.ShowMessageAsync($"Path not found: {path}", "Open Recent");
            return;
        }
        await OpenProjectAsync(path);
    }

    [RelayCommand]
    private void RemoveRecentProject(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _recentFileService.Remove(path);
        RefreshRecentProjects();
    }

    [RelayCommand]
    private void TogglePinRecentProject(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var pinned = RecentProjects.FirstOrDefault(p => p.Path == path)?.IsPinned ?? false;
        _recentFileService.SetPinned(path, !pinned);
        RefreshRecentProjects();
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        _recentFileService.Clear(AppOptions.ClearRecentKeepsPinned);
        RefreshRecentProjects();
    }

    /// <summary>The configured default language, or the one the most recent project used when detection is on.</summary>
    internal string PreferredDefaultLanguage() =>
        PreferredLanguageDetector.Resolve(AppOptions.DefaultLanguage, AppOptions.DetectLanguageFromRecent, _recentFileService.LoadRecent());

    internal void RefreshRecentProjects()
    {
        _recentFileService.Limit = AppOptions.RecentProjectsLimit;
        RecentProjects = new ObservableCollection<Project>(_recentFileService.LoadRecent());
    }

    /// <summary>
    /// Every open path (picker, recent, new project, import, startup argument) funnels through here.
    /// </summary>
    public async Task OpenProjectAsync(string path)
    {
        if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;

        FlushPendingEdits();

        // A newer open supersedes one still scanning.
        _openCts?.Cancel();
        var cts = _openCts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            if (cts.IsCancellationRequested) return;
            StatusText = $"Scanning… {p.DirectoriesScanned:N0} folders, {p.FilesFound:N0} files";
            LoadDetail = p.CurrentDirectory is { } d ? Path.GetRelativePath(path, d) : string.Empty;
        });

        IsLoading = true;
        CanCancelLoad = true;
        LoadDetail = string.Empty;
        StatusText = "Loading project…";
        try
        {
            var result = await _lifecycleService.OpenProjectAsync(path, progress, cts.Token);
            switch (result.Status)
            {
                case ProjectOpenStatus.Success:
                    CurrentPath = path;
                    AppOptions.LastProjectPath = path;
                    _preferenceService.Save(AppOptions);
                    LoadFromStore(path);
                    ShowStartScreen = false;
                    StatusText = $"Opened {ProjectName}";
                    if (result.Warnings is { Count: > 0 })
                        await _messageService.ShowMessageAsync(string.Join("\n\n", result.Warnings), "Format limitations");
                    break;
                case ProjectOpenStatus.Cancelled:
                    StatusText = cts.IsCancellationRequested ? "Load cancelled" : string.Empty;
                    break;
                default:
                    await _messageService.ShowMessageAsync(result.ErrorMessage ?? $"Failed to open project: {result.Status}", "Open Project");
                    StatusText = string.Empty;
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            await _messageService.ShowMessageAsync($"Error loading project: {ex.Message}", "Open Project");
            StatusText = string.Empty;
        }
        finally
        {
            // Only the newest open owns the loading state; a superseded one must not clear it.
            if (_openCts == cts)
            {
                _openCts = null;
                IsLoading = false;
                CanCancelLoad = false;
                LoadDetail = string.Empty;
            }
            cts.Dispose();
            RefreshRecentProjects();
            if (!HasProject) ShowStartScreen = true;
        }
    }

    /// <summary>Aborts a project open that is still scanning the folder (e.g. an accidentally chosen huge folder).</summary>
    [RelayCommand]
    private void CancelLoad()
    {
        _openCts?.Cancel();
        StatusText = "Cancelling…";
    }

    /// <summary>
    /// Rebuilds all UI state from the translation store. Used after open, and after the lifecycle
    /// service reloads or merges external changes from disk.
    /// </summary>
    internal void LoadFromStore(string path)
    {
        AllTranslation = _translationStore.Translations.ToList();
        ProjectSettings = _lifecycleService.CurrentProject ?? ProjectSettings.LoadFrom(path);
        RefreshFileFormatStatus();
        RefreshProviderChoices();
        _undoRedoService.Clear();

        HiddenNamespaces = new ObservableCollection<string>(ProjectSettings?.HiddenNamespaces ?? []);

        // Missing (language, key) pairs are materialized as empty values so every card shows every language.
        // They start out "saved" so opening a project doesn't mark it dirty.
        var added = AddMissingTranslationsCore();
        if (added.Count > 0)
        {
            _translationStore.AddItems(added);
            _translationStore.MarkSaved(added);
        }

        PrimaryLanguage = ResolvePrimaryLanguage();
        if (!string.IsNullOrEmpty(path)) _recentFileService.SetPrimaryLanguage(path, PrimaryLanguage);
        StatusBarService.Instance.UpdateProjectName(ProjectName);
        if (StatusBarService.Instance.ViewModel is { } sb)
        {
            sb.AvailableLanguages.Clear();
            foreach (var lang in OrderedLanguages()) sb.AvailableLanguages.Add(lang);
        }

        ClearSessionDirtyState();
        IsDirty = false;
        ValidationIssues.Clear();
        OnPropertyChanged(nameof(HasValidationIssues));
        SourceCodeUsages.Clear();
        SourceCodeScanned = false;
        ClearSearchResults();

        RefreshTree();
        UpdateSummaryInfo();
        InitLanguageVisibilityFilter(OrderedLanguages());
        if (EditorMode == EditorMode.Review) ApplyReviewFilter();
        else Search(SearchText);

        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(WindowTitle));

        if (ProjectSettings?.AutoScanOnOpen == true) _ = ScanSourceCode();
    }

    /// <summary>The project's primary language, matched against the languages actually present (e.g. "en-US" vs "en").</summary>
    private string ResolvePrimaryLanguage()
    {
        var languages = ProjectLanguages();
        var configured = ProjectSettings?.PrimaryLanguage;
        if (!string.IsNullOrEmpty(configured))
        {
            var exact = languages.FirstOrDefault(l => string.Equals(l, configured, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var prefix = languages.FirstOrDefault(l => l.StartsWith(configured.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (prefix != null) return prefix;
        }
        var preferred = PreferredDefaultLanguage();
        return languages.FirstOrDefault(l => string.Equals(l, preferred, StringComparison.OrdinalIgnoreCase))
            ?? languages.OrderBy(l => l, StringComparer.Ordinal).FirstOrDefault()
            ?? configured
            ?? "en-US";
    }

    /// <summary>
    /// Languages found in the translation files. A brand-new project has no entries yet, so its
    /// languages come from the manifest instead. (Core's default settings for a folder without a
    /// manifest always declare "en-US", so the manifest list can't be merged in unconditionally.)
    /// </summary>
    internal List<string> ProjectLanguages()
    {
        var fromFiles = AllTranslation.ToLanguages().Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return fromFiles.Count > 0
            ? fromFiles
            : (ProjectSettings?.Languages ?? []).Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal IEnumerable<string> OrderedLanguages()
    {
        var primary = PrimaryLanguage;
        return ProjectLanguages()
            .OrderBy(l => string.Equals(l, primary, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l, StringComparer.Ordinal);
    }

    // ───────────────────────── Save / Close ─────────────────────────

    private bool CanSave() => IsDirty && HasProject;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        FlushPendingEdits();
        SyncStore();
        var result = await _lifecycleService.SaveProjectAsync();
        switch (result.Status)
        {
            case ProjectSaveStatus.Success:
                AfterSuccessfulSave();
                break;

            case ProjectSaveStatus.ValidationErrors:
                var errors = result.Errors ?? [];
                var msg = $"{errors.Count} validation error(s) found:\n"
                    + string.Join("\n", errors.Take(5).Select(e => $"• [{e.Language}] {e.Namespace}: {e.Message}"))
                    + (errors.Count > 5 ? $"\n… and {errors.Count - 5} more" : string.Empty);
                if (await _messageService.ConfirmAsync(msg + "\n\nSave anyway?", "Validation Errors", "Save Anyway", "Cancel"))
                {
                    ForceSave();
                }
                else
                {
                    ShowValidationResults(errors);
                }
                break;

            case ProjectSaveStatus.FileSystemError:
                await _messageService.ShowMessageAsync($"Save failed: {result.ErrorMessage}", "Save");
                break;
        }
    }

    /// <summary>Writes files without running validation (after the user accepted validation errors).</summary>
    private void ForceSave()
    {
        if (ProjectSettings == null) return;
        try
        {
            _projectService.Save(ProjectSettings, [], _translationStore.Translations);
            ProjectSettings.Save();
            _translationStore.MarkAllSaved();
            AfterSuccessfulSave();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _messageService.ShowMessageAsync($"Save failed: {ex.Message}", "Save");
        }
    }

    private void AfterSuccessfulSave()
    {
        FeedTranslationMemory(SessionDirtyKeys);
        ClearSessionDirtyState();
        IsDirty = false;
        StatusText = $"Saved {DateTime.Now:t}";
    }

    [RelayCommand]
    private async Task SaveTo()
    {
        if (!HasProject) return;
        var selected = await _dialogService.SelectFolderAsync(CurrentPath, "Save Project To");
        if (selected == null) return;

        FlushPendingEdits();
        SyncStore();
        var result = await _lifecycleService.SaveProjectAsAsync(selected);
        if (result.Status == ProjectSaveStatus.Success)
        {
            CurrentPath = selected;
            ProjectSettings = _lifecycleService.CurrentProject;
            AppOptions.LastProjectPath = selected;
            _preferenceService.Save(AppOptions);
            AfterSuccessfulSave();
            RefreshRecentProjects();
        }
        else if (result.Status != ProjectSaveStatus.Cancelled)
        {
            await _messageService.ShowMessageAsync($"Save As failed: {result.ErrorMessage}", "Save As");
        }
    }

    [RelayCommand]
    private async Task CloseProject()
    {
        if (await TryCloseProjectAsync()) ResetUiAfterClose();
    }

    /// <summary>Closes the project, prompting to save if needed. Returns false if the user cancelled.</summary>
    public async Task<bool> TryCloseProjectAsync()
    {
        if (!HasProject) return true;
        FlushPendingEdits();
        SyncStore();

        // Approvals and renames aren't visible to the store's dirty tracking; prompt for them here.
        if (_hasUntrackedChanges && !_translationStore.IsDirty)
        {
            var choice = await _messageService.ChooseAsync("You have unsaved changes. Save before closing?", "Unsaved Changes", "Save", "Don't Save");
            if (choice == ChoiceResult.Cancel) return false;
            if (choice == ChoiceResult.Primary) ForceSave();
            _hasUntrackedChanges = false;
        }

        return await _lifecycleService.CloseProjectAsync() == CloseResult.Closed;
    }

    private void ResetUiAfterClose()
    {
        foreach (var g in PagingController.Data) g.Dispose();
        AllTranslation = [];
        CurrentPath = string.Empty;
        ProjectSettings = null;
        StatusBarService.Instance.ViewModel?.Encoding.Update("UTF-8");
        StatusBarService.Instance.ViewModel?.LineEndings.Update(Environment.NewLine == "\r\n" ? "CRLF" : "LF");
        _primaryLanguage = null;
        SelectedNode = null;
        SelectedGroup = null;
        StatusText = string.Empty;
        ShowStartScreen = true;
        HiddenNamespaces.Clear();
        ValidationIssues.Clear();
        OnPropertyChanged(nameof(HasValidationIssues));
        ClearSearchResults();
        SourceCodeUsages.Clear();
        _undoRedoService.Clear();
        CurrentTreeItems.Clear();
        CurrentFlatItems.Clear();
        LanguageVisibilityFilter.Clear();
        PagingController.SwapData([]);
        ClearSessionDirtyState();
        IsDirty = false;
        UpdateSummaryInfo();
        PagedUpdates();
        StatusBarService.Instance.UpdateProjectName(Locales.Loc.T("No project"));
        RefreshRecentProjects();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (HasProject) await OpenProjectAsync(CurrentPath);
    }

    [RelayCommand]
    private void RevealInFileManager()
    {
        if (HasProject) PlatformService.RevealInFileManager(CurrentPath);
    }

    // ───────────────────────── Import / Export ─────────────────────────

    private string FormatForExtension(string ext) =>
        _strategyFactory.SaveStrategies
            .FirstOrDefault(st => st.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))?.FormatId
        ?? FormatIds.Json;

    [RelayCommand]
    private async Task Import()
    {
        if (!HasProject) return;
        var file = await _dialogService.SelectFileAsync(CurrentPath, "Import Translations", TranslationFileFilters);
        if (file == null) return;

        try
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var loader = _strategyFactory.GetLoadStrategy(FormatForExtension(ext))
                ?? throw new NotSupportedException($"No loader is available for {ext} files.");
            var imported = loader.Load(Path.GetDirectoryName(file)!).ToList();
            MergeImported(imported, Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or NotSupportedException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            await _messageService.ShowMessageAsync($"Import failed: {ex.Message}", "Import");
        }
    }

    [RelayCommand]
    private async Task ImportExcel()
    {
        if (!HasProject) return;
        var file = await _dialogService.SelectFileAsync(CurrentPath, "Import from Excel", [new FileFilter("Excel workbook", "*.xlsx")]);
        if (file == null) return;

        try
        {
            MergeImported(ExcelService.Import(file), Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            await _messageService.ShowMessageAsync($"Excel import failed: {ex.Message}", "Import");
        }
    }

    private void MergeImported(List<TranslationItem> imported, string source)
    {
        if (imported.Count == 0)
        {
            _ = _messageService.ShowMessageAsync("No translations found in the selected file.", "Import");
            return;
        }

        var index = AllTranslation.GroupBy(t => (t.Namespace, t.Language)).ToDictionary(g => g.Key, g => g.First());
        var changed = new List<TranslationItem>();
        foreach (var item in imported.Where(i => !string.IsNullOrWhiteSpace(i.Namespace)))
        {
            if (index.TryGetValue((item.Namespace, item.Language), out var existing))
            {
                if (existing.Value == item.Value) continue;
                existing.Value = item.Value;
                changed.Add(existing);
            }
            else
            {
                AllTranslation.Add(item);
                index[(item.Namespace, item.Language)] = item;
                changed.Add(item);
            }
        }

        var added = AddMissingTranslationsCore();
        MarkStructureChanged(changed.Select(c => c.Namespace));
        NotifyBulkValueChanges(changed.Concat(added));
        RefreshTree();
        UpdateSummaryInfo();
        Search(SearchText, true);
        StatusText = $"Imported {changed.Count} value(s) from {source}";
    }

    [RelayCommand]
    private async Task Export()
    {
        if (AllTranslation.Count == 0)
        {
            await _messageService.ShowMessageAsync("No translations to export.", "Export");
            return;
        }

        var labels = ExportFormats.Select(f => f.Label).ToList();
        var choice = await _dialogService.ShowPickAsync("Export Translations", "Choose an output format. Files are written into the folder you pick next.", labels, labels[0]);
        if (choice == null) return;
        var formatId = ExportFormats.First(f => f.Label == choice).FormatId;

        var folder = await _dialogService.SelectFolderAsync(CurrentPath, "Export To Folder");
        if (folder == null) return;

        try
        {
            FlushPendingEdits();
            _projectService.Save(folder, formatId, CurrentTreeItems.ToList(), AllTranslation.ForParse().ToList());
            StatusText = $"Exported {choice} to {folder}";
            PlatformService.RevealInFileManager(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            await _messageService.ShowMessageAsync($"Export failed: {ex.Message}", "Export");
        }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (AllTranslation.Count == 0)
        {
            await _messageService.ShowMessageAsync("No translations to export.", "Export");
            return;
        }

        var file = await _dialogService.SaveFileAsync(CurrentPath, $"{ProjectName}.xlsx", "Export to Excel", [new FileFilter("Excel workbook", "*.xlsx")]);
        if (file == null) return;

        try
        {
            FlushPendingEdits();
            ExcelService.Export(file, AllTranslation.ForParse().ToList());
            StatusText = $"Exported to {file}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _messageService.ShowMessageAsync($"Excel export failed: {ex.Message}", "Export");
        }
    }
}
