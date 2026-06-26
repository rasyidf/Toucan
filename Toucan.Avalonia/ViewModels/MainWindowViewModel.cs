using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Toucan.Avalonia.Models;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private List<TranslationItem> allTranslation = new();
    [ObservableProperty] private NsTreeItem? selectedNode;
    [ObservableProperty] private SummaryInfoViewModel summaryInfo = new();
    [ObservableProperty] private PaginationViewModel<LanguageGroupViewModel> pagingController;
    [ObservableProperty] private ObservableCollection<NsTreeItem> currentTreeItems = new();
    [ObservableProperty] private AppOptions appOptions;
    [ObservableProperty] private string currentPath = string.Empty;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private ProjectSettings? projectSettings;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isTreeView = true;
    [ObservableProperty] private bool showStartScreen = true;
    [ObservableProperty] private bool languagesVisible = true;
    [ObservableProperty] private bool showAdvancedOptions;
    [ObservableProperty] private IEnumerable<LanguageGroupViewModel> pageData = [];
    [ObservableProperty] private string pageMessage = string.Empty;
    [ObservableProperty] private int paginationWindow = 1;
    [ObservableProperty] private ObservableCollection<PaginationButton> pageButtons = new();

    private readonly IRecentProjectService _recentFileService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly IPreferenceService _preferenceService;
    private readonly IBulkActionService? _bulkActionService;
    private readonly IPretranslationService? _pretranslationService;
    private readonly IProjectService _projectService;
    private readonly Func<string, LanguageGroupViewModel> _languageGroupFactory;

    public MainWindowViewModel(
        IRecentProjectService recentFileService,
        IDialogService dialogService,
        IMessageService messageService,
        IPreferenceService preferenceService,
        IProjectService projectService,
        IBulkActionService? bulkActionService = null,
        IPretranslationService? pretranslationService = null,
        Func<string, LanguageGroupViewModel>? languageGroupFactory = null)
    {
        _recentFileService = recentFileService;
        _dialogService = dialogService;
        _messageService = messageService;
        _preferenceService = preferenceService;
        _projectService = projectService;
        _bulkActionService = bulkActionService;
        _pretranslationService = pretranslationService;
        _languageGroupFactory = languageGroupFactory ?? (ns => new LanguageGroupViewModel(ns));

        AppOptions = _preferenceService.Load();
        int pageSize = AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
        int maxItems = AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, new List<LanguageGroupViewModel>(), maxItems);
        PagedUpdates();
    }

    #region File Commands

    [RelayCommand]
    private async Task NewFolder()
    {
        // ponytail: full new-project dialog deferred to Batch 5 (dialogs)
        var folder = await _dialogService.SelectFolderAsync(CurrentPath);
        if (string.IsNullOrEmpty(folder)) return;
        CurrentPath = folder;
        AppOptions.LastProjectPath = folder;
        _recentFileService.Add(folder);
        await LoadFolderAsync(folder);
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        var selected = await _dialogService.SelectFolderAsync(CurrentPath);
        if (string.IsNullOrEmpty(selected)) return;
        CurrentPath = selected;
        AppOptions.LastProjectPath = selected;
        _recentFileService.Add(selected);
        await LoadFolderAsync(CurrentPath);
    }

    [RelayCommand]
    private async Task OpenProjectFile()
    {
        var selected = await _dialogService.SelectFileAsync(CurrentPath, "Toucan project|*.project;*.json");
        if (string.IsNullOrEmpty(selected)) return;
        string directory = Path.GetDirectoryName(selected) ?? CurrentPath;
        if (!Directory.Exists(directory)) { _messageService.ShowMessage($"Folder not found: {directory}"); return; }
        CurrentPath = directory;
        AppOptions.LastProjectPath = directory;
        _recentFileService.Add(directory);
        await LoadFolderAsync(directory);
    }

    [RelayCommand]
    private async Task OpenRecent()
    {
        var recents = _recentFileService.LoadRecent();
        if (recents.Count == 0) { _messageService.ShowMessage("No recent projects found."); return; }
        var path = recents.First().Path;
        if (!Directory.Exists(path)) { _messageService.ShowMessage($"Path not found: {path}"); return; }
        CurrentPath = path;
        await LoadFolderAsync(CurrentPath);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        IsDirty = false;
        if (ProjectSettings != null)
            _projectService.Save(ProjectSettings, CurrentTreeItems.ToList(), AllTranslation);
        else
            _projectService.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation);
    }

    private bool CanSave() => IsDirty;

    [RelayCommand]
    private async Task SaveTo()
    {
        var folder = await _dialogService.SelectFolderAsync(CurrentPath);
        if (string.IsNullOrEmpty(folder)) return;
        CurrentPath = folder;
        Save();
    }

    [RelayCommand]
    private void CloseProject()
    {
        AllTranslation = new();
        CurrentPath = string.Empty;
        SelectedNode = null;
        IsDirty = false;
        StatusText = string.Empty;
        CurrentTreeItems.Clear();
        PagingController?.SwapData(new List<LanguageGroupViewModel>());
        PageButtons.Clear();
        RefreshTree();
        UpdateSummaryInfo();
    }

    [RelayCommand]
    private async Task Refresh() => await LoadFolderAsync(CurrentPath);

    [RelayCommand]
    private void Exit()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    #endregion

    #region Edit Commands

    [RelayCommand]
    private void NewLanguage()
    {
        // ponytail: LanguagePrompt dialog wired in Batch 5
    }

    [RelayCommand]
    private void NewItem()
    {
        // ponytail: PromptDialog wired in Batch 5
    }

    [RelayCommand(CanExecute = nameof(CanModifyItem))]
    private void RenameItem()
    {
        // ponytail: PromptDialog wired in Batch 5
    }

    [RelayCommand(CanExecute = nameof(CanModifyItem))]
    private void DeleteItem()
    {
        if (SelectedNode == null) return;
        DeleteItemCore(SelectedNode);
    }

    private bool CanModifyItem() => SelectedNode != null;

    #endregion

    #region Pagination

    [RelayCommand] private void NextPage() { PagingController.NextPage(); PagedUpdates(); }
    [RelayCommand] private void PreviousPage() { PagingController.PreviousPage(); PagedUpdates(); }
    [RelayCommand] private void FirstPage() { PagingController.MoveFirst(); PagedUpdates(); }
    [RelayCommand] private void LastPage() { PagingController.LastPage(); PagedUpdates(); }
    [RelayCommand] private void GoToPage(int? page)
    {
        if (page == null || page < 1 || page > PagingController.Pages) return;
        PagingController.Page = page.Value;
        PagedUpdates();
    }

    internal void PagedUpdates()
    {
        PageData = PagingController.PageData;
        PageMessage = PagingController.PageMessage;
        UpdatePageButtons(PaginationWindow);
    }

    private void UpdatePageButtons(int window = 1)
    {
        PageButtons.Clear();
        if (PagingController == null) return;
        int pages = Math.Max(1, PagingController.Pages);
        int current = Math.Min(Math.Max(1, PagingController.Page), pages);
        if (pages <= 0) return;
        PageButtons.Add(new PaginationButton(1, false, current == 1));
        if (pages == 1) return;
        int start = Math.Max(2, current - window);
        int end = Math.Min(pages - 1, current + window);
        if (start > 2) PageButtons.Add(new PaginationButton(0, true, false));
        for (int i = start; i <= end; i++) PageButtons.Add(new PaginationButton(i, false, i == current));
        if (end < pages - 1) PageButtons.Add(new PaginationButton(0, true, false));
        PageButtons.Add(new PaginationButton(pages, false, current == pages));
    }

    #endregion

    #region View Commands

    [RelayCommand] private void ToggleViewMode() => IsTreeView = !IsTreeView;
    [RelayCommand] private void ToggleLanguagesVisibility() => LanguagesVisible = !LanguagesVisible;
    [RelayCommand] private void ToggleAdvancedOptions() => ShowAdvancedOptions = !ShowAdvancedOptions;

    [RelayCommand]
    private void ShowAll()
    {
        Search("", true);
    }

    #endregion

    #region Bulk / PreTranslate

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) { _messageService.ShowMessage("No translations loaded."); return; }
        if (_bulkActionService == null) { _messageService.ShowMessage("Bulk action service is not available."); return; }
        await _bulkActionService.PreTranslateAsync(AllTranslation);
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = "Pre-translation completed.";
    }

    [RelayCommand]
    private void GenerateStatisticsBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) { _messageService.ShowMessage("No translations loaded."); return; }
        if (_bulkActionService == null) { _messageService.ShowMessage("Bulk action service is not available."); return; }
        var stats = _bulkActionService.GenerateStatistics(AllTranslation);
        StatusText = stats;
        _messageService.ShowMessage(stats);
    }

    [RelayCommand]
    private void DeleteUnusedTranslations()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) { _messageService.ShowMessage("No translations loaded."); return; }
        if (!_messageService.ShowConfirmation("Delete all IDs that have no translation values for any language?")) return;
        var emptyNamespaces = AllTranslation.GroupBy(t => t.Namespace).Where(g => g.All(i => string.IsNullOrEmpty(i.Value))).Select(g => g.Key).ToList();
        foreach (var ns in emptyNamespaces) AllTranslation.RemoveAll(o => o.Namespace == ns);
        RefreshTree(); UpdateSummaryInfo(); IsDirty = true;
        StatusText = $"Deleted {emptyNamespaces.Count} unused IDs";
    }

    [RelayCommand]
    internal void AddMissingTranslations()
    {
        var namespaces = AllTranslation.ToNamespaces().ToList();
        var allLanguages = AllTranslation.ToLanguages().ToList();
        foreach (string lang in allLanguages)
        {
            var languageNamespaces = AllTranslation.OnlyLanguage(lang).ToNamespaces();
            AllTranslation.AddRange(namespaces.Except(languageNamespaces).Select(o => new TranslationItem { Namespace = o, Value = string.Empty, Language = lang }));
        }
    }

    [RelayCommand]
    private void ShowUntranslated()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) { _messageService.ShowMessage("No translations loaded."); return; }
        var matched = AllTranslation.Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList();
        if (matched.Count == 0) { _messageService.ShowMessage("No untranslated items found."); return; }
        var groups = matched.ToNamespaces().Select(n => { var g = _languageGroupFactory(n); g.LoadTranslations(matched.Where(o => o.Namespace == n).ToList()); return g; }).ToList();
        PagingController.SwapData(groups, false);
        PagedUpdates();
    }

    [RelayCommand] private void ClearFilter() { SearchText = string.Empty; Search("", true); }

    #endregion

    #region Search

    partial void OnSearchTextChanged(string value) => Search(value, false);

    internal void Search(string ns, bool alwaysPaging = false)
    {
        var matched = AllTranslation.ToList();
        List<TranslationItem> items;
        if (string.IsNullOrWhiteSpace(ns)) items = matched;
        else if (!ns.EndsWith('.')) items = matched.Where(o => o.Namespace == ns).ToList();
        else
        {
            var translations = matched.Where(o => o.Namespace.StartsWith(ns)).ToList();
            if (!alwaysPaging && translations.Count / 3 > AppOptions.TruncateResultsOver)
                items = translations.Take(AppOptions.TruncateResultsOver).ToList();
            else items = translations;
        }

        var namespaces = items.ToNamespaces().ToList();
        var groups = namespaces.Select(n => { var g = _languageGroupFactory(n); g.LoadTranslations(matched.Where(o => o.Namespace == n).ToList()); return g; }).ToList();
        PagingController.SwapData(groups, false);
        PagedUpdates();
    }

    #endregion

    #region Help

    [RelayCommand] private void HelpHomepage() => OpenUrl("https://rasyid.dev");

    [RelayCommand]
    private async Task HelpAbout()
    {
        var window = GetMainWindow();
        if (window == null) return;
        var dlg = new Views.Dialogs.AboutDialog();
        await dlg.ShowDialog(window);
    }

    [RelayCommand]
    private async Task ShowPreferences()
    {
        var window = GetMainWindow();
        if (window == null) return;
        var vm = new OptionsViewModel(_preferenceService);
        var dlg = new Views.Dialogs.OptionsDialog(vm);
        var result = await dlg.ShowDialog<bool?>(window);
        if (result == true)
        {
            AppOptions = vm.AppOptions;
            StatusBarService.Instance.UpdateDefaultLanguage(AppOptions.DefaultLanguage);
            // Rebuild paging with new page size
            int pageSize = AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
            int maxItems = AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;
            var currentData = PagingController?.Data ?? new System.Collections.ObjectModel.ObservableCollection<LanguageGroupViewModel>();
            PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, currentData, maxItems);
            PagedUpdates();
        }
    }

    [RelayCommand]
    private async Task ShowProjectSettings()
    {
        if (ProjectSettings == null) { _messageService.ShowMessage("No project loaded."); return; }
        var window = GetMainWindow();
        if (window == null) return;
        var vm = new ProjectSettingsDialogViewModel(ProjectSettings);
        var dlg = new Views.Dialogs.ProjectSettingsDialog(vm);
        var result = await dlg.ShowDialog<bool?>(window);
        if (result == true)
        {
            StatusBarService.Instance.UpdateProjectName(ProjectSettings.Name ?? "");
            StatusBarService.Instance.UpdateDefaultLanguage(ProjectSettings.PrimaryLanguage);
        }
    }

    [RelayCommand]
    private async Task ShowLanguageManager()
    {
        if (ProjectSettings == null) { _messageService.ShowMessage("No project loaded."); return; }
        var window = GetMainWindow();
        if (window == null) return;
        var vm = new LanguageManagerViewModel(ProjectSettings);
        var dlg = new Views.Dialogs.LanguageManagerDialog(vm);
        var result = await dlg.ShowDialog<bool?>(window);
        if (result == true)
        {
            StatusBarService.Instance.UpdateDefaultLanguage(ProjectSettings.PrimaryLanguage);
            // Add any new languages to translations
            foreach (var lang in ProjectSettings.Languages)
                if (!AllTranslation.Any(t => t.Language == lang))
                    AddLanguage(lang);
            UpdateSummaryInfo();
        }
    }

    private static global::Avalonia.Controls.Window? GetMainWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    #endregion

    #region Internal Helpers

    partial void OnSelectedNodeChanged(NsTreeItem? value)
    {
        (RenameItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (DeleteItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
        StatusBarService.Instance.UpdateCursor(value != null ? $"Selected: {value.Namespace}" : "Ln 0, Col 0");
    }

    internal void RefreshTree()
    {
        var nodes = AllTranslation.ForParse().ToNsTree();
        CurrentTreeItems.Clear();
        foreach (var node in nodes) CurrentTreeItems.Add(node);
    }

    internal void UpdateSummaryInfo()
    {
        SummaryInfo.Update(AllTranslation);
        try { StatusBarService.Instance.ShowNotificationBadge(SummaryInfo.Details?.Sum(d => (int)d.Missing) ?? 0); } catch { }
    }

    private async Task LoadFolderAsync(string path)
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading project...";
            StatusBarService.Instance.SetLoading(true);

            var result = await Task.Run(() => _projectService.LoadProject(path));
            ProjectSettings = result.Settings;
            AllTranslation = result.Translations;
            CurrentPath = result.Settings.ProjectPath;

            AddMissingTranslations();
            RefreshTree();
            UpdateSummaryInfo();
            ShowStartScreen = false;
            IsDirty = false;
            Search("", true);

            StatusBarService.Instance.UpdateProjectName(result.Settings.Name ?? Path.GetFileName(path));
            StatusBarService.Instance.UpdateDefaultLanguage(result.Settings.PrimaryLanguage);
        }
        catch (Exception ex) { _messageService.ShowMessage($"Error loading project: {ex.Message}"); }
        finally { IsLoading = false; StatusText = "Ready"; StatusBarService.Instance.SetLoading(false); }
    }

    public void CreateNewItem(string newNamespace)
    {
        if (string.IsNullOrWhiteSpace(newNamespace)) return;
        if (AllTranslation.NoEmpty().Any(s => s.Namespace.Contains(newNamespace))) { _messageService.ShowMessage("Duplicate name"); return; }
        var languages = AllTranslation.ToLanguages().ToList();
        foreach (var lang in languages) AllTranslation.Add(new TranslationItem { Namespace = newNamespace, Value = string.Empty, Language = lang });
        RefreshTree(newNamespace);
        UpdateSummaryInfo();
        Search(newNamespace, true);
    }

    public void AddLanguage(string newLanguage)
    {
        if (string.IsNullOrWhiteSpace(newLanguage)) return;
        if (AllTranslation.Any(s => s.Language == newLanguage)) { _messageService.ShowMessage("Duplicate language"); return; }
        AllTranslation.Add(new TranslationItem { Namespace = "", Value = "", Language = newLanguage });
        AddMissingTranslations();
        UpdateSummaryInfo();
        RefreshTree();
        Search("", true);
    }

    public void RenameItemCore(NsTreeItem node, string newName)
    {
        if (node == null || string.IsNullOrWhiteSpace(newName) || newName.Contains('.')) return;
        string oldNs = node.Namespace;
        string newNs = oldNs[..oldNs.LastIndexOf(node.Name)] + newName.Trim();
        AllTranslation.ForParse().ToList().ForEach(item =>
        {
            if (item.Namespace.StartsWith(oldNs)) item.Namespace = item.Namespace.Replace(oldNs, newNs);
        });
        RefreshTree(newNs);
        Search(newNs, true);
    }

    public void DeleteItemCore(NsTreeItem node)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.Namespace)) return;
        if (node.Parent == null) CurrentTreeItems.Remove(node);
        AllTranslation.RemoveAll(o => o?.Namespace?.StartsWith(node.Namespace) ?? false);
        RefreshTree();
        Search("", true);
    }

    private void RefreshTree(string selectNamespace = "") => RefreshTree();

    internal static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }
        catch { }
    }

    #endregion
}
