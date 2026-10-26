using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
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
        _strategyFactory.SaveStrategies.Where(st => st is not IResourceCapabilities c || c.Permissions(new() { FormatId = st.FormatId }).EditKeys).Select(st => (st.DisplayName, st.FormatId)).ToList();

    [ObservableProperty] private ObservableCollection<Project> recentProjects = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    partial void OnRecentProjectsChanged(ObservableCollection<Project> value) => OnPropertyChanged(nameof(HasRecentProjects));

    // ───────────────────────── New / Open ─────────────────────────

    [RelayCommand]
    private async Task NewFolder(string? frameworkName)
    {
        var vm = await _dialogService.ShowNewProjectAsync(frameworkName);
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
        var selected = await _dialogService.SelectFileAsync(CurrentPath, "Open Translation Project",
            [new FileFilter("Translation project", "*.tproj", "*.project", "*.babel"), new FileFilter("All files", "*")]);
        if (selected == null) return;

        var directory = Path.GetDirectoryName(selected);
        if (directory == null || !Directory.Exists(directory))
        {
            await _messageService.ShowMessageAsync($"Folder not found: {directory}", "Open Project");
            return;
        }
        await OpenProjectAsync(selected);
    }

    [RelayCommand]
    private async Task ImportProject()
    {
        var vm = await _dialogService.ShowImportProjectAsync();
        if (vm?.IsValid != true) return;
        if (ProjectSettings.LoadFrom(vm.Folder) != null) { await OpenProjectAsync(vm.Folder); return; }
        try
        {
            var settings = ProjectSettings.CreateDefault(vm.Folder);
            settings.Framework = vm.SelectedProfile!.Id;
            settings.SaveFormat = vm.SelectedProfile.DefaultFormatId;
            settings.Languages = vm.DetectedLanguages.ToList();
            settings.PrimaryLanguage = settings.Languages.FirstOrDefault() ?? "en";
            if (vm.SelectedProfile is IFrameworkFeatures features)
            {
                settings.ResourceDocuments = vm.DiscoveredFiles.Select(f => new ResourceDocumentState { Resource = features.Describe(f) }).ToList();
                foreach (var document in settings.ResourceDocuments)
                {
                    var loaded = _strategyFactory.ReadResource(new(settings.ProjectPath, document.Resource));
                    if (loaded.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error))
                        throw new FormatException(string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
                }
                settings.Save();
            }
            await OpenProjectAsync(vm.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CompatibilityImportException or System.Text.Json.JsonException)
        { await _messageService.ShowMessageAsync($"Project import failed: {ex.Message}", "Import Project"); }
    }

    [RelayCommand]
    private async Task ShowMigrationReport()
    {
        if (ProjectSettings?.LastMigrationReport is { } report) await _dialogService.ShowMigrationReportAsync(report, false);
        else await _messageService.ShowMessageAsync("No retained migration report for this project.", "Migration report");
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
        MigrationReport? migrationReport = null;
        var folder = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : path;
        if (Directory.Exists(folder))
        {
            var candidates = _compatibilityImporters.SelectMany(i => i.FindProjects(folder)).Select(Path.GetFullPath).ToList();
            var native = Path.Combine(folder, "toucan.tproj");
            if (!File.Exists(path) && !File.Exists(native) && candidates.Count > 1)
            {
                var selected = await _dialogService.ShowPickAsync("Choose migration project", "Select the exact project to preview. Each source project remains unchanged.", candidates);
                if (selected == null) return;
                path = selected;
            }
            else if (!File.Exists(path) && !File.Exists(native) && candidates.Count == 1) path = candidates[0];
            if (File.Exists(path) && candidates.Contains(Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                if (File.Exists(native))
                {
                    var selected = await _dialogService.ShowPickAsync("Project selection", "A native project exists. Choose which project to open.", [native, Path.GetFullPath(path)], native);
                    if (selected == null) return;
                    path = selected;
                }
                if (!Path.GetFullPath(path).Equals(Path.GetFullPath(native), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    try
                    {
                        migrationReport = await Task.Run(() => MigrationReport.Inspect(path, _projectService));
                        if (!await _dialogService.ShowMigrationReportAsync(migrationReport, true)) return;
                        if (!migrationReport.InputsUnchanged(folder)) { await _messageService.ShowMessageAsync("Migration inputs changed. Open a new preview.", "Migration"); return; }
                    }
                    catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or InvalidOperationException)
                    { await _messageService.ShowMessageAsync(ex.Message, "Migration preview"); return; }
                }
            }
        }

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
            var result = migrationReport == null ? await _lifecycleService.OpenProjectAsync(path, progress, cts.Token) : await _lifecycleService.OpenMigrationAsync(path, migrationReport, progress, cts.Token);
            switch (result.Status)
            {
                case ProjectOpenStatus.Success:
                    path = _lifecycleService.CurrentProject!.ProjectPath;
                    if (migrationReport != null) _lifecycleService.CurrentProject.LastMigrationReport = migrationReport;
                    CurrentPath = path;
                    AppOptions.LastProjectPath = path;
                    _preferenceService.Save(AppOptions);
                    LoadFromStore(path);
                    BumpWorkspaceRevision();
                    ShowStartScreen = false;
                    StatusText = $"Opened {ProjectName}";
                    if (result.Warnings is { Count: > 0 })
                        await _messageService.ShowMessageAsync(string.Join("\n\n", result.Warnings), "Format limitations");
                    await OfferRecoveryAsync(path, result);
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

    /// <summary>
    /// After a crash or interrupted save: first offers to put back the files from before the interrupted save, then to
    /// recover unsaved edits. Declining leaves everything in place; cancelling keeps the offer for next time.
    /// </summary>
    private async Task OfferRecoveryAsync(string path, ProjectOpenResult result)
    {
        if (result.InterruptedSave)
        {
            var choice = await _messageService.ChooseAsync(
                "Toucan stopped while saving this project, so some files may be only partly updated.\n\nRestore the files from before that save, or keep what is on disk now?",
                "Interrupted save", "Restore previous files", "Keep current files");
            if (choice == ChoiceResult.Primary)
            {
                var restore = await _lifecycleService.RestoreInterruptedSaveAsync();
                LoadFromStore(path);
                if (!restore.Complete)
                    await _messageService.ShowMessageAsync($"{restore.Failed.Count} file(s) could not be restored: {string.Join(", ", restore.Failed.Select(f => Path.GetFileName(f.File)))}", "Interrupted save");
            }
            else if (choice == ChoiceResult.Secondary) _lifecycleService.KeepInterruptedSave();
        }

        if (_lifecycleService.PendingRecovery is { } draft)
        {
            var choice = await _messageService.ChooseAsync(
                $"Toucan found {draft.ChangeCount} unsaved change(s) from {draft.SavedAtUtc.ToLocalTime():g}, probably from a crash or an interrupted save.\n\nRecover them? Values that were changed on disk since then are left as they are.",
                "Recover unsaved changes", "Recover", "Discard");
            if (choice == ChoiceResult.Primary)
            {
                var applied = _lifecycleService.ApplyRecovery();
                LoadFromStore(path);
                // Deletions and approvals are invisible to the store's dirty tracking, so anything applied counts as unsaved.
                if (applied.Applied > 0) _hasUntrackedChanges = true;
                IsDirty = _translationStore.IsDirty || applied.Applied > 0;
                StatusText = $"Recovered {applied.Applied} unsaved change(s)";
                if (applied.Conflicts.Count > 0)
                    await _messageService.ShowMessageAsync(
                        $"{applied.Conflicts.Count} change(s) were not recovered because the files changed on disk:\n"
                        + string.Join("\n", applied.Conflicts.Take(8).Select(c => $"• [{c.Language}] {c.Namespace}")),
                        "Recover unsaved changes");
            }
            else if (choice == ChoiceResult.Secondary) _lifecycleService.DiscardRecovery();
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
        if (!Workspace.IsOpen && ProjectSettings is not null) Workspace.Open(ProjectSettings);
        RefreshFileFormatStatus();
        RefreshProviderChoices();

        HiddenNamespaces = new ObservableCollection<string>(ProjectSettings?.HiddenNamespaces ?? []);

        // Missing (language, key) pairs are materialized as empty values so every card shows every language.
        // They start out "saved" so opening a project doesn't mark it dirty.
        var added = Workspace.Source is null ? AddMissingTranslationsCore() : [];
        if (added.Count > 0)
        {
            _translationStore.AddItems(added);
            _translationStore.MarkSaved(added);
            Workspace.MaterializeMissing(added);
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
        IsDirty = Workspace.IsDirty;
        ValidationIssues.Clear();
        ValidationLastChecked = null;
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationIssues));
        _sourceScanCancellation?.Cancel();
        _sourceScanCancellation = null;
        IsScanning = false;
        (_sourceCodeService as IProjectSourceCodeService)?.Reset();
        SourceScanDiagnostics.Clear();
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
    private Task Save() => PersistAsync(new SaveOptions());

    /// <summary>
    /// Saves through the lifecycle service and reports the outcome. Validation findings never block a save: they are
    /// shown in the Issues panel. Returns true when everything is on disk.
    /// </summary>
    private async Task<bool> PersistAsync(SaveOptions options)
    {
        FlushPendingEdits();
        SyncStore();
        var result = await _lifecycleService.SaveProjectAsync(options);
        switch (result.Status)
        {
            case ProjectSaveStatus.Success:
                AfterSuccessfulSave();
                if (result.Findings is { Count: > 0 } findings)
                {
                    ShowValidationResults(findings);
                    var errorCount = findings.Count(f => f.Severity == ValidationSeverity.Error);
                    if (errorCount > 0) StatusText = $"Saved {DateTime.Now:t} with {errorCount} validation error(s)";
                }
                return true;

            case ProjectSaveStatus.ExternalChanges:
                var names = string.Join(", ", (result.ExternalFiles ?? []).Select(Path.GetFileName));
                if (await _messageService.ConfirmAsync(
                        $"These files were changed outside Toucan since you opened or last saved them:\n{names}\n\nNothing was saved. Overwrite them with your version?",
                        "Files changed outside Toucan", "Overwrite", "Cancel"))
                    return await PersistAsync(options with { OverwriteExternalChanges = true });
                return false;

            case ProjectSaveStatus.ValidationErrors:
                ShowValidationResults(result.Errors ?? []);
                await _messageService.ShowMessageAsync($"{result.Errors?.Count ?? 0} validation error(s) must be fixed first. Nothing was saved.", "Validation Errors");
                return false;

            case ProjectSaveStatus.FileSystemError:
                NotifyOutcome(Locales.Loc.T("Save failed"), result.ErrorMessage ?? Locales.Loc.T("The files could not be saved. Your edits remain in the workspace."),
                    Toucan.Plugins.NotificationSeverity.Error, "toucan.file.save", Locales.Loc.T("Retry save"));
                await _messageService.ShowMessageAsync($"Save failed: {result.ErrorMessage}", "Save");
                return false;

            default:
                return false;
        }
    }

    private void AfterSuccessfulSave()
    {
        RefreshDocumentNavigation();
        FeedTranslationMemory(SessionDirtyKeys);
        ClearSessionDirtyState();
        IsDirty = Workspace.IsDirty;
        StatusText = $"Saved {DateTime.Now:t}";
        NotifyOutcome(Locales.Loc.T("Project saved"), ProjectName, Toucan.Plugins.NotificationSeverity.Success);
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
            if (choice == ChoiceResult.Primary && !await PersistAsync(new SaveOptions())) return false;
            _hasUntrackedChanges = false;
        }

        return await _lifecycleService.CloseProjectAsync() == CloseResult.Closed;
    }

    private void ResetUiAfterClose()
    {
        RefreshDocumentNavigation();
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
        ValidationLastChecked = null;
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasValidationIssues));
        ClearSearchResults();
        _sourceScanCancellation?.Cancel();
        _sourceScanCancellation = null;
        IsScanning = false;
        (_sourceCodeService as IProjectSourceCodeService)?.Reset();
        SourceScanDiagnostics.Clear();
        SourceCodeUsages.Clear();
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
        BumpWorkspaceRevision();
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
            FlushPendingEdits();
            var revision = _workspaceRevision;
            var before = CaptureStructure();
            var table = ExcelService.Read(file, _spreadsheetDialects);
            var preview = SpreadsheetPlanner.Plan(table, before, ProjectSettings?.ResourceDocuments.Select(d => d.Resource));
            if (ProjectSettings?.RequireValidForApproval == true)
            {
                var errors = _validationPipeline.RunAll(new ValidationContext { Items = preview.Items, PrimaryLanguage = PrimaryLanguage })
                    .Where(r => r.Severity == ValidationSeverity.Error).ToList();
                var changedUnits = preview.Changes.Select(c => (c.Namespace, c.Language)).ToHashSet();
                var approvals = preview.Items.Where(i => i.IsApproved && changedUnits.Contains((i.Namespace, i.Language)));
                var conflicts = approvals.Where(i => errors.Any(e => e.Namespace == i.Namespace && (e.Language == null || e.Language == i.Language)))
                    .Select(i => $"Approval blocked by validation: {i.Namespace} [{i.Language}].");
                preview = preview with { Conflicts = preview.Conflicts.Concat(conflicts).ToList() };
            }
            if (!await _dialogService.ShowSpreadsheetPreviewAsync(preview) || !preview.CanApply) return;
            FlushPendingEdits();
            if (revision != _workspaceRevision)
                throw new InvalidOperationException("The workspace changed during preview. Import again to review the current changes.");
            AllTranslation = preview.Items.Select(i => ResourceEntryOperations.Copy(i)).ToList();
            _undoRedoService.RecordStructure(before, AllTranslation);
            var touched = preview.Changes.Select(c => (c.Namespace, c.Language)).ToHashSet();
            MarkStructureChanged(preview.Changes.Select(c => c.Namespace));
            NotifyBulkValueChanges(AllTranslation.Where(i => touched.Contains((i.Namespace, i.Language))));
            RefreshTree();
            UpdateSummaryInfo();
            Search(SearchText, true);
            ShowValidationResults(_validationPipeline.RunAll(new ValidationContext { Items = AllTranslation, PrimaryLanguage = PrimaryLanguage }).ToList());
            StatusText = $"Imported {preview.Changes.Count} change(s) from {Path.GetFileName(file)}. {preview.Summary}";
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
        if (imported.Any(i => index.TryGetValue((i.Namespace, i.Language), out var existing)
            ? !existing.Permissions.EditValues && existing.Value != i.Value || !existing.Permissions.EditMetadata &&
                (i.FormatData?.GetValueOrDefault("exchange.comment") == "True" && existing.Comment != i.Comment ||
                 i.FormatData?.GetValueOrDefault("exchange.approved") == "True" && existing.IsApproved != i.IsApproved)
            : !CanChangeKeys))
        {
            _ = _messageService.ShowMessageAsync("Resource capabilities forbid this import. Generated catalogs must be extracted from source.", "Import");
            return;
        }
        var changed = new List<TranslationItem>();
        foreach (var item in imported.Where(i => !string.IsNullOrWhiteSpace(i.Namespace)))
        {
            if (index.TryGetValue((item.Namespace, item.Language), out var existing))
            {
                var hasComment = item.FormatData?.GetValueOrDefault("exchange.comment") == "True";
                var hasApproval = item.FormatData?.GetValueOrDefault("exchange.approved") == "True";
                if (existing.Value == item.Value && (!hasComment || existing.Comment == item.Comment) &&
                    (!hasApproval || existing.IsApproved == item.IsApproved)) continue;
                existing.Value = item.Value;
                if (hasComment) existing.Comment = item.Comment;
                if (hasApproval) existing.IsApproved = item.IsApproved;
                changed.Add(existing);
            }
            else
            {
                AllTranslation.Add(item);
                index[(item.Namespace, item.Language)] = item;
                changed.Add(item);
            }
        }

        var added = Workspace.Source is null ? AddMissingTranslationsCore() : [];
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

        var dialectLabel = await _dialogService.ShowPickAsync("Spreadsheet dialect", "Choose a workbook format.",
            _spreadsheetDialects.Select(d => d.Label).ToList(), ExcelService.Toucan.Label);
        if (dialectLabel == null) return;
        var dialect = _spreadsheetDialects.Single(d => d.Label == dialectLabel);
        var file = await _dialogService.SaveFileAsync(CurrentPath, $"{ProjectName}.xlsx", "Export to Excel", [new FileFilter("Excel workbook", "*.xlsx")]);
        if (file == null) return;

        try
        {
            FlushPendingEdits();
            ExcelService.Write(file, AllTranslation.ForParse().ToList(), dialect);
            StatusText = $"Exported to {file}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            await _messageService.ShowMessageAsync($"Excel export failed: {ex.Message}", "Export");
        }
    }
}
