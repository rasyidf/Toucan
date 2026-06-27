using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
                CurrentPath = vm.ProjectFolder;
                AppOptions.LastProjectPath = CurrentPath;
                _recentFileService.Add(CurrentPath);
                await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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
            CurrentPath = selected;
            AppOptions.LastProjectPath = selected;
            _recentFileService.Add(selected);
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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

            CurrentPath = result.Value.Folder;
            AppOptions.LastProjectPath = CurrentPath;
            _recentFileService.Add(CurrentPath);
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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

        CurrentPath = directory;
        AppOptions.LastProjectPath = directory;
        _recentFileService.Add(directory);
        await LoadFolderAsync(directory).ConfigureAwait(true);
    }

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
            // update status bar default language from project manifest (toucan.project) if present
            try
            {
                var manifestPath = System.IO.Path.Combine(path, "toucan.project");
                if (System.IO.File.Exists(manifestPath))
                {
                    var txt = System.IO.File.ReadAllText(manifestPath);
                    try
                    {
                        var root = JsonNode.Parse(txt)?.AsObject();
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
                    // fallback: use first language in loaded translations if available
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
            // ensure project name shown in status bar
            try
            {
                StatusBarService.Instance.UpdateProjectName(System.IO.Path.GetFileName(path) ?? path ?? "Toucan Project");
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
    private void Save()
    {
        // Run validation before save
        if (_validationPipeline != null && AllTranslation?.Count > 0)
        {
            ValidationContext ctx = new()
            {
                Items = AllTranslation,
                Settings = ProjectSettings.LoadFrom(CurrentPath) ?? ProjectSettings.CreateDefault(CurrentPath)
            };
            List<ValidationResult> errors = _validationPipeline.RunAll(ctx)
                .Where(r => r.Severity == Toucan.Core.Contracts.ValidationSeverity.Error)
                .ToList();

            if (errors.Count > 0)
            {
                var msg = $"{errors.Count} validation error(s) found:\n" +
                    string.Join("\n", errors.Take(5).Select(e => $"• [{e.Language}] {e.Namespace}: {e.Message}"));
                if (errors.Count > 5)
                {
                    msg += $"\n... and {errors.Count - 5} more";
                }

                if (!_messageService.ShowConfirmation(msg + "\n\nSave anyway?", "Validation Errors"))
                {
                    return;
                }
            }
        }

        IsDirty = false;
        _projectService?.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation ?? []);
    }

    private bool CanSave()
    {
        return IsDirty;
    }

    [RelayCommand]
    private void SaveTo()
    {
        var selected = _dialogService.SelectFolder(CurrentPath);
        if (selected != null)
        {
            CurrentPath = selected;
            Save();
        }
    }

    [RelayCommand]
    private void CloseProject()
    {
        // Reset all project-related data and UI state
        AllTranslation = [];
        CurrentPath = string.Empty;
        SelectedNode = null!;
        IsDirty = false;
        StatusText = string.Empty;
        ShowStartScreen = true;

        // Clear collected tree and paging controller
        CurrentTreeItems.Clear();
        PagingController?.SwapData(new List<LanguageGroupViewModel>());
        PageButtons.Clear();

        // Reset summary and UI
        RefreshTree();
        UpdateSummaryInfo();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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

        CurrentPath = path;
        await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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

        CurrentPath = path;
        AppOptions.LastProjectPath = path;
        _recentFileService.Add(path);
        await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
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
            var file = System.IO.Path.Combine(path, "translations.xlsx");
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
