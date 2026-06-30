using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

/// <summary>
/// File operations: open, save, close, import, export, recent projects, refresh, reveal in explorer.
/// </summary>
internal partial class MainWindowViewModel
{
    #region File Menu

    [RelayCommand]
    private async Task NewFolder()
    {
        if (_projectService == null)
        {
            _messageService.ShowMessage("Project service is not available.");
            return;
        }

        if (_dialogService.ShowNewProject(_projectService, out var vm) && vm != null)
        {
            if (string.IsNullOrWhiteSpace(vm.ProjectFolder))
            {
                _messageService.ShowMessage("No folder selected.");
                return;
            }

            try
            {
                await OpenProjectViaLifecycleAsync(vm.ProjectFolder).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"Failed to load project: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        string? selected = _dialogService.SelectFolder(CurrentPath);

        if (selected != null)
        {
            await OpenProjectViaLifecycleAsync(selected).ConfigureAwait(true);
        }
        else
        {
            _messageService.ShowMessage("No folder selected.");
        }
    }

    [RelayCommand]
    private async Task ImportProject()
    {
        if (_dialogService.ShowImportProject(out var vm) && vm != null)
        {
            var result = vm.GetResult();
            if (result == null)
            {
                return;
            }

            await OpenProjectViaLifecycleAsync(result.Value.Folder).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenProjectFile()
    {
        string? selected = _dialogService.SelectFile(CurrentPath, "Toucan project|*.project|JSON files (*.json)|*.json|All Files (*.*)|*.*");

        if (string.IsNullOrEmpty(selected))
        {
            _messageService.ShowMessage("No project file selected.");
            return;
        }

        // If user selected a file, take its directory as the project folder
        string directory = Path.GetDirectoryName(selected) ?? CurrentPath;
        if (!Directory.Exists(directory))
        {
            _messageService.ShowMessage($"Folder not found: {directory}");
            return;
        }

        await OpenProjectViaLifecycleAsync(directory).ConfigureAwait(true);
    }

    /// <summary>
    /// Delegates project opening to IProjectLifecycleService, then updates UI state on success.
    /// All open paths (folder picker, file picker, recent, new project, import) funnel through here.
    /// </summary>
    private async Task OpenProjectViaLifecycleAsync(string path)
    {
        if (_lifecycleService != null)
        {
            IsLoading = true;
            ShowStartScreen = false;
            StatusText = "Loading project...";

            try
            {
                var result = await _lifecycleService.OpenProjectAsync(path).ConfigureAwait(true);

                if (result.Status == ProjectOpenStatus.Success)
                {
                    CurrentPath = path;
                    AppOptions.LastProjectPath = path;
                    UpdateUiAfterProjectLoad(path);
                }
                else if (result.Status == ProjectOpenStatus.Cancelled)
                {
                    // User cancelled unsaved-changes prompt, restore previous state
                    if (string.IsNullOrEmpty(CurrentPath))
                        ShowStartScreen = true;
                }
                else
                {
                    _messageService.ShowMessage(result.ErrorMessage ?? $"Failed to open project: {result.Status}");
                    if (string.IsNullOrEmpty(CurrentPath))
                        ShowStartScreen = true;
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"Error loading project: {ex.Message}");
                if (string.IsNullOrEmpty(CurrentPath))
                    ShowStartScreen = true;
            }
            finally
            {
                IsLoading = false;
                StatusText = "Ready";
            }
        }
        else
        {
            // Fallback: use legacy LoadFolderAsync if lifecycle service is not available
            CurrentPath = path;
            AppOptions.LastProjectPath = path;
            _recentFileService.Add(path);
            await LoadFolderAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Updates UI state after a successful project load from the lifecycle service.
    /// Pulls translations from ITranslationManagementService and refreshes tree/summary/paging.
    /// </summary>
    private void UpdateUiAfterProjectLoad(string path)
    {
        // Get translations from the management service (lifecycle service already initialized it)
        if (_translationManagement != null)
        {
            AllTranslation = _translationManagement.Translations.ToList();
        }

        // Load hidden namespaces from project settings
        var projSettings = ProjectSettings.LoadFrom(path);
        HiddenNamespaces.Clear();
        if (projSettings?.HiddenNamespaces is { Count: > 0 })
        {
            foreach (var ns in projSettings.HiddenNamespaces)
                HiddenNamespaces.Add(ns);
        }

        AddMissingTranslations();
        RefreshTree();
        UpdateSummaryInfo();
        IsDirty = false;

        // Update status bar default language from project manifest
        try
        {
            var manifestPath = Path.Combine(path, "toucan.project");
            if (File.Exists(manifestPath))
            {
                var txt = File.ReadAllText(manifestPath);
                try
                {
                    var root = System.Text.Json.Nodes.JsonNode.Parse(txt)?.AsObject();
                    var primary = root?["primaryLanguage"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(primary))
                    {
                        StatusBarService.Instance.UpdateDefaultLanguage(primary);
                    }
                }
                catch { }
            }
            else
            {
                var firstLang = AllTranslation?.ToLanguages().FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstLang))
                {
                    StatusBarService.Instance.UpdateDefaultLanguage(firstLang);
                }
            }
        }
        catch { }

        // Populate paging controller for loaded data
        Search("", true);

        // Ensure project name shown in status bar
        try
        {
            StatusBarService.Instance.UpdateProjectName(Path.GetFileName(path) ?? path ?? "Toucan Project");
        }
        catch { }
    }

    /// <summary>
    /// Legacy load method used as fallback when IProjectLifecycleService is not available.
    /// </summary>
    private async Task LoadFolderAsync(string path)
    {
        if (_projectService == null)
        {
            _messageService.ShowMessage("Project service is not available.");
            return;
        }

        try
        {
            IsLoading = true;
            ShowStartScreen = false;
            StatusText = "Loading project...";

            var loaded = await Task.Run(() => _projectService.Load(path)).ConfigureAwait(true);
            AllTranslation = loaded;
            AddMissingTranslations();
            RefreshTree();
            UpdateSummaryInfo();
            IsDirty = false;

            // Update status bar default language from project manifest
            try
            {
                var manifestPath = Path.Combine(path, "toucan.project");
                if (File.Exists(manifestPath))
                {
                    var txt = File.ReadAllText(manifestPath);
                    try
                    {
                        var root = System.Text.Json.Nodes.JsonNode.Parse(txt)?.AsObject();
                        var primary = root?["primaryLanguage"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(primary))
                        {
                            StatusBarService.Instance.UpdateDefaultLanguage(primary);
                        }
                    }
                    catch { }
                }
                else
                {
                    var firstLang = AllTranslation?.ToLanguages().FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(firstLang))
                    {
                        StatusBarService.Instance.UpdateDefaultLanguage(firstLang);
                    }
                }
            }
            catch { }

            Search("", true);

            try
            {
                StatusBarService.Instance.UpdateProjectName(Path.GetFileName(path) ?? path ?? "Toucan Project");
            }
            catch { }
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Error loading project: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            StatusText = "Ready";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_lifecycleService != null)
        {
            var result = await _lifecycleService.SaveProjectAsync().ConfigureAwait(true);

            switch (result.Status)
            {
                case ProjectSaveStatus.Success:
                    IsDirty = false;
                    SessionDirtyKeys.Clear();
                    SessionDirtyCount = 0;
                    ClearDirtyGroups();
                    break;

                case ProjectSaveStatus.ValidationErrors:
                    // Show validation errors and let user decide
                    var errors = result.Errors ?? [];
                    var msg = $"{errors.Count} validation error(s) found:\n" +
                        string.Join("\n", errors.Take(5).Select(e => $"• [{e.Language}] {e.Namespace}: {e.Message}"));
                    if (errors.Count > 5)
                        msg += $"\n... and {errors.Count - 5} more";

                    if (_messageService.ShowConfirmation(msg + "\n\nSave anyway?", "Validation Errors"))
                    {
                        // Force save by calling the legacy path (bypasses validation)
                        SaveLegacy();
                    }
                    break;

                case ProjectSaveStatus.FileSystemError:
                    _messageService.ShowMessage($"Save failed: {result.ErrorMessage}");
                    break;

                case ProjectSaveStatus.Cancelled:
                    break;
            }
        }
        else
        {
            // Fallback: legacy save logic
            SaveLegacy();
        }
    }

    /// <summary>
    /// Legacy save logic used as fallback when lifecycle service is unavailable,
    /// or when a force-save is needed after user confirms validation errors.
    /// </summary>
    private void SaveLegacy()
    {
        IsDirty = false;
        SessionDirtyKeys.Clear();
        SessionDirtyCount = 0;
        ClearDirtyGroups();
        _projectService?.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation ?? []);
    }

    private bool CanSave()
    {
        return IsDirty;
    }

    [RelayCommand]
    private async Task SaveTo()
    {
        var selected = _dialogService.SelectFolder(CurrentPath);
        if (selected == null)
            return;

        if (_lifecycleService != null)
        {
            var result = await _lifecycleService.SaveProjectAsAsync(selected).ConfigureAwait(true);
            if (result.Status == ProjectSaveStatus.Success)
            {
                CurrentPath = selected;
                AppOptions.LastProjectPath = selected;
                IsDirty = false;
            }
            else if (result.Status != ProjectSaveStatus.Cancelled)
            {
                _messageService.ShowMessage($"Save As failed: {result.ErrorMessage}");
            }
        }
        else
        {
            CurrentPath = selected;
            SaveLegacy();
        }
    }

    [RelayCommand]
    private async Task CloseProject()
    {
        if (_lifecycleService != null)
        {
            var result = await _lifecycleService.CloseProjectAsync().ConfigureAwait(true);
            if (result == CloseResult.Closed)
            {
                ResetUiAfterClose();
            }
            // If cancelled, do nothing — user chose not to close
        }
        else
        {
            // Legacy close logic
            ResetUiAfterClose();
        }
    }

    /// <summary>
    /// Resets all UI state after a project is closed.
    /// </summary>
    private void ResetUiAfterClose()
    {
        AllTranslation = [];
        CurrentPath = string.Empty;
        SelectedNode = null!;
        IsDirty = false;
        StatusText = string.Empty;
        ShowStartScreen = true;

        CurrentTreeItems.Clear();
        PagingController?.SwapData(new List<LanguageGroupViewModel>());
        PageButtons.Clear();

        RefreshTree();
        UpdateSummaryInfo();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_lifecycleService != null && !string.IsNullOrEmpty(CurrentPath))
        {
            await OpenProjectViaLifecycleAsync(CurrentPath).ConfigureAwait(true);
        }
        else
        {
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    internal async Task OpenRecent()
    {
        // Reload the list for the flyout menu
        RefreshRecentProjects();
        // If there are recent projects, open the most recent
        var recents = _recentFileService.LoadRecent();
        if (recents.Count == 0)
        {
            _messageService.ShowMessage("No recent projects found.");
            return;
        }

        string path = recents.First().Path;
        if (!Directory.Exists(path))
        {
            _messageService.ShowMessage($"Path not found: {path}");
            return;
        }

        await OpenProjectViaLifecycleAsync(path).ConfigureAwait(true);
    }

    // Recent projects collection for flyout menu binding
    [ObservableProperty]
    private ObservableCollection<Project> recentProjects = [];

    internal void RefreshRecentProjects()
    {
        var list = _recentFileService.LoadRecent();
        RecentProjects.Clear();
        foreach (var p in list)
        {
            RecentProjects.Add(p);
        }
    }

    [RelayCommand]
    private async Task OpenRecentProject(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            _messageService.ShowMessage($"Path not found: {path}");
            _recentFileService.Remove(path);
            RefreshRecentProjects();
            return;
        }

        await OpenProjectViaLifecycleAsync(path).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        foreach (var p in RecentProjects.ToList())
        {
            _recentFileService.Remove(p.Path);
        }

        RecentProjects.Clear();
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath))
        {
            return;
        }

        _ = Process.Start(new ProcessStartInfo("explorer.exe", CurrentPath) { UseShellExecute = true });
    }

    #endregion

    #region Import / Export

    // ponytail: file filter string built from the SaveStyles enum names
    private const string ImportExportFilter =
        "JSON (*.json)|*.json|" +
        "YAML (*.yml;*.yaml)|*.yml;*.yaml|" +
        "TOML (*.toml)|*.toml|" +
        "Android XML (*.xml)|*.xml|" +
        "iOS Strings (*.strings)|*.strings|" +
        "XLIFF (*.xlf;*.xliff)|*.xlf;*.xliff|" +
        "ARB - Flutter (*.arb)|*.arb|" +
        "CSV (*.csv)|*.csv|" +
        "RESX (*.resx)|*.resx|" +
        "PO/Gettext (*.po)|*.po|" +
        "INI (*.ini)|*.ini|" +
        "All Files (*.*)|*.*";

    private static SaveStyles FilterIndexToStyle(int index)
    {
        return index switch
        {
            1 => SaveStyles.Json,
            2 => SaveStyles.Yaml,
            3 => SaveStyles.Toml,
            4 => SaveStyles.AndroidXml,
            5 => SaveStyles.IosStrings,
            6 => SaveStyles.Xliff,
            7 => SaveStyles.Arb,
            8 => SaveStyles.Csv,
            9 => SaveStyles.Resx,
            10 => SaveStyles.Properties,
            11 => SaveStyles.Adb,
            _ => SaveStyles.Json
        };
    }

    [RelayCommand]
    private void Import()
    {
        var file = _dialogService.SelectFile(CurrentPath, ImportExportFilter);
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            var folder = Path.GetDirectoryName(file)!;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var style = ext switch
            {
                ".json" => SaveStyles.Json,
                ".yml" or ".yaml" => SaveStyles.Yaml,
                ".toml" => SaveStyles.Toml,
                ".xml" => SaveStyles.AndroidXml,
                ".strings" => SaveStyles.IosStrings,
                ".xlf" or ".xliff" => SaveStyles.Xliff,
                ".arb" => SaveStyles.Arb,
                ".csv" => SaveStyles.Csv,
                ".resx" => SaveStyles.Resx,
                ".po" => SaveStyles.Properties,
                ".ini" => SaveStyles.Adb,
                _ => SaveStyles.Json
            };

            var loader = _strategyFactory?.GetLoadStrategy(style);
            if (loader == null)
            {
                _messageService.ShowMessage($"No loader available for {ext} format.");
                return;
            }

            List<TranslationItem> imported = loader.Load(folder).ToList();
            if (imported.Count == 0)
            {
                _messageService.ShowMessage("No translations found in the selected file.");
                return;
            }

            // Merge imported items into current project
            AllTranslation ??= [];
            foreach (var item in imported)
            {
                var existing = AllTranslation.FirstOrDefault(t => t.Namespace == item.Namespace && t.Language == item.Language);
                if (existing != null)
                {
                    existing.Value = item.Value;
                }
                else
                {
                    AllTranslation.Add(item);
                }
            }

            RefreshTree();
            UpdateSummaryInfo();
            Search("", true);
            IsDirty = true;
            StatusText = $"Imported {imported.Count} items from {Path.GetFileName(file)}";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations to export.");
            return;
        }

        var folder = _dialogService.SelectFolder(CurrentPath);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        // Ask user for format via a simple prompt listing options
        var formatInput = _dialogService.ShowPrompt("Export Format",
            "Enter format: json, yaml, toml, xml, strings, xliff, arb, csv, resx, po, ini",
            "json");
        if (string.IsNullOrWhiteSpace(formatInput))
        {
            return;
        }

        var style = formatInput.Trim().ToLowerInvariant() switch
        {
            "json" => SaveStyles.Json,
            "yaml" or "yml" => SaveStyles.Yaml,
            "toml" => SaveStyles.Toml,
            "xml" or "android" => SaveStyles.AndroidXml,
            "strings" or "ios" => SaveStyles.IosStrings,
            "xliff" or "xlf" => SaveStyles.Xliff,
            "arb" or "flutter" => SaveStyles.Arb,
            "csv" => SaveStyles.Csv,
            "resx" => SaveStyles.Resx,
            "po" or "gettext" => SaveStyles.Properties,
            "ini" => SaveStyles.Adb,
            _ => SaveStyles.Json
        };

        try
        {
            _projectService?.Save(folder, style, CurrentTreeItems.ToList(), AllTranslation);
            StatusText = $"Exported to {folder} ({style})";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Export failed: {ex.Message}");
        }
    }

    #endregion

    #region Excel Import / Export

    [RelayCommand]
    private void ExportExcel()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations to export.");
            return;
        }

        var path = _dialogService.SelectFolder(CurrentPath);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var file = Path.Combine(path, "translations.xlsx");
            Toucan.Core.Services.ExcelService.Export(file, AllTranslation);
            StatusText = $"Exported to {file}";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Excel export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ImportExcel()
    {
        var file = _dialogService.SelectFile(CurrentPath, "Excel files (*.xlsx)|*.xlsx");
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            var imported = Toucan.Core.Services.ExcelService.Import(file);
            if (imported.Count == 0)
            {
                _messageService.ShowMessage("No translations found in the Excel file.");
                return;
            }

            AllTranslation ??= [];
            foreach (var item in imported)
            {
                var existing = AllTranslation.FirstOrDefault(t => t.Namespace == item.Namespace && t.Language == item.Language);
                if (existing != null)
                {
                    existing.Value = item.Value;
                }
                else
                {
                    AllTranslation.Add(item);
                }
            }

            RefreshTree();
            UpdateSummaryInfo();
            Search("", true);
            IsDirty = true;
            StatusText = $"Imported {imported.Count} items from Excel";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Excel import failed: {ex.Message}");
        }
    }

    #endregion
}
