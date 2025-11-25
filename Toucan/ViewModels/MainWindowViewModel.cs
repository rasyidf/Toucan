using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Services;
using Toucan.Views;
using Toucan.Models;

namespace Toucan.ViewModels;

internal partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private List<TranslationItem> allTranslation;

    [ObservableProperty]
    private NsTreeItem? selectedNode;

    [ObservableProperty]
    private SummaryInfoViewModel summaryInfo = new();

    [ObservableProperty]
    private PaginationViewModel<LanguageGroupViewModel> pagingController;

    [ObservableProperty]
    private ObservableCollection<NsTreeItem> currentTreeItems = new();


    [ObservableProperty]
    private AppOptions appOptions;

    [ObservableProperty]
    private string currentPath;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string searchText;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isTreeView = true;

    [ObservableProperty]
    private bool showStartScreen = true;

    // New properties for languages panel visibility and advanced options
    [ObservableProperty]
    private bool languagesVisible = true;

    [ObservableProperty]
    private bool showAdvancedOptions = false;


    [ObservableProperty]
    private IEnumerable<LanguageGroupViewModel> pageData;

    [ObservableProperty]
    private string pageMessage;

    [ObservableProperty]
    private int paginationWindow = 1; // show 1 page on each side = 3 items centered

    // Page button model for UI - moved to shared model Toucan.Models.PaginationButton

    [ObservableProperty]
    private ObservableCollection<PaginationButton> pageButtons = new();

    private readonly IRecentProjectService _recentFileService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly IPreferenceService _preferenceService;
    private readonly IBulkActionService _bulkActionService;
    private readonly Toucan.Core.Contracts.Services.IPretranslationService? _pretranslationService;
    private readonly IProjectService _projectService;
    private readonly System.Func<NewProjectPrompt> _newProjectPromptFactory;


    public MainWindowViewModel(
        IRecentProjectService recentFileService,
        IDialogService dialogService,
        IMessageService messageService,
        IPreferenceService preferenceService,
        IBulkActionService bulkActionService = null,
        Toucan.Core.Contracts.Services.IPretranslationService pretranslationService = null,
        IProjectService projectService = null)
    {
        _recentFileService = recentFileService;
        _dialogService = dialogService;
        _messageService = messageService;
        _preferenceService = preferenceService;
        _bulkActionService = bulkActionService;
        _pretranslationService = pretranslationService;
        _projectService = projectService;
        _newProjectPromptFactory = null;
        

        AppOptions = _preferenceService.Load();

        // Initialize PagingController after loading options
        int pageSize = AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
        int maxItems = AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, new List<LanguageGroupViewModel>(), maxItems);
        PagedUpdates();
    }

        

    [RelayCommand]
    void HelpHomepage()
    {
        // Redirect to Rasyid.dev
        OpenUrl("https://rasyid.dev");
    }
            

    [RelayCommand]
    void HelpAbout()
    {
        AboutDialog ad = new(Application.Current.MainWindow);
        ad.ShowDialog();
    }

     

    #region Pagination

    [RelayCommand]
    private void NextPage()
    {
        PagingController.NextPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        PagingController.PreviousPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void FirstPage()
    {
        PagingController.MoveFirst();
        PagedUpdates();
    }

    [RelayCommand]
    private void LastPage()
    {
        PagingController.LastPage();
        PagedUpdates();
    }

    internal void PagedUpdates()
    {
        PageData = PagingController.PageData;
        PageMessage = PagingController.PageMessage;
        // Notify property changed for other UI consumers
        OnPropertyChanged(nameof(PageData));
        OnPropertyChanged(nameof(PageMessage));
        UpdatePageButtons(PaginationWindow);
        // populate controller values for the pagination usercontrol
        // the control will bind to these properties (PageButtons/PageMessage)
        OnPropertyChanged(nameof(PageButtons));
        OnPropertyChanged(nameof(PageMessage));
    }

    private void UpdatePageButtons(int window = 1)
    {
        // window is how many pages on each side of the current page to show
        PageButtons.Clear();
        if (PagingController == null)
            return;
        if (PagingController == null)
            return;
        int pages = Math.Max(1, PagingController.Pages);
        int current = Math.Min(Math.Max(1, PagingController.Page), pages);

        if (pages <= 0)
            return;

        // always show first page
        PageButtons.Add(new PaginationButton(1, default, current == 1));

        if (pages == 1)
            return;

        int start = Math.Max(2, current - window);
        int end = Math.Min(pages - 1, current + window);

        if (start > 2)
        {
            // ellipse
            PageButtons.Add(new PaginationButton(default, true, default));
        }

        for (int i = start; i <= end; i++)
        {
            PageButtons.Add(new PaginationButton(i, default, i == current));
        }

        if (end < pages - 1)
        {
            PageButtons.Add(new PaginationButton(default, true, default));
        }

        // always show last page
        PageButtons.Add(new PaginationButton(pages, default, current == pages));
    }

    [RelayCommand]
    private void GoToPage(int? page)
    {
        if (PagingController == null) return;
        if (page == null) return;
        if (page < 1 || page > PagingController.Pages) return;
        PagingController.Page = page.Value;
        PagedUpdates();
    }
    #endregion

    [RelayCommand]
    private void ToggleViewMode()
    {
        IsTreeView = !IsTreeView;
    }

    // New commands for UI control and bulk actions
    [RelayCommand]
    private void ToggleLanguagesVisibility()
    {
        LanguagesVisible = !LanguagesVisible;
    }

    [RelayCommand]
    private void ToggleAdvancedOptions()
    {
        ShowAdvancedOptions = !ShowAdvancedOptions;
    }

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to pre-translate.");
            return;
        }

        // If we have a UI-capable pretranslation engine, show the Pre-Translate dialog instead
        if (_pretranslationService != null)
        {
            // gather language list
            var languages = AllTranslation.ToLanguages().ToList();

            var vm = new PreTranslateViewModel(languages, AllTranslation, _pretranslationService);
            var dialog = new Views.Dialogs.PreTranslateWindow(vm) { Owner = Application.Current.MainWindow };
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                UpdateSummaryInfo();
                IsDirty = true;
                StatusText = "Pre-translation completed.";
            }

            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        await _bulkActionService.PreTranslateAsync(AllTranslation).ConfigureAwait(false);
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = "Pre-translation completed.";
    }

    [RelayCommand]
    private void GenerateStatisticsBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to generate statistics.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        var stats = _bulkActionService.GenerateStatistics(AllTranslation);
        StatusText = stats;
        _messageService.ShowMessage(stats);
    }

    // Per-language / contextual pre-translate and stats commands
    [RelayCommand]
    private async Task PreTranslateLanguage(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null) return;

            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage("No translations loaded to pre-translate.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            var toTranslate = AllTranslation.Where(t => t.Language == item.Language).ToList();
            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            IsDirty = true;
            StatusText = $"Pre-translation completed for {item.Language}.";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreTranslateNamespace(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null) return;

            var dialog = new PromptDialog("Pre-translate namespace", "Enter namespace (exact or prefix) to pre-translate for language: " + item.Language, "")
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true) return;

            string ns = dialog.ResponseText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ns)) return;

            var toTranslate = AllTranslation.Where(t => t.Language == item.Language && t.Namespace != null && (t.Namespace == ns || t.Namespace.StartsWith(ns))).ToList();

            if (toTranslate.Count == 0)
            {
                _messageService.ShowMessage("No translations matched the namespace.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            IsDirty = true;
            StatusText = $"Pre-translation completed for {item.Language} (namespace: {ns}).";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreTranslateKey(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null) return;

            var dialog = new PromptDialog("Pre-translate key", "Enter exact key namespace/id to pre-translate for language: " + item.Language, "")
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true) return;

            string key = dialog.ResponseText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return;

            var toTranslate = AllTranslation.Where(t => t.Language == item.Language && t.Namespace == key).ToList();

            if (toTranslate.Count == 0)
            {
                _messageService.ShowMessage("No matching key found for that language.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            IsDirty = true;
            StatusText = $"Pre-translation completed for {item.Language} (key: {key}).";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GenerateStatisticsForLanguage(SummaryItem item)
    {
        try
        {
            IsLoading = true;
            if (item == null) return;

            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage("No translations loaded to generate statistics.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            var langItems = AllTranslation.Where(t => t.Language == item.Language);
            var stats = _bulkActionService.GenerateStatistics(langItems);
            StatusText = stats;
            _messageService.ShowMessage(stats);
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error generating statistics: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void HideLanguage(SummaryItem item)
    {
        if (item == null) return;

        // For now 'hide' removes the summary entry from the panel. This is a UI-level action.
        SummaryInfo.Details.Remove(item);
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(url);
        }
        catch
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&", StringComparison.InvariantCulture);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                throw;
            }
        }
    }
    internal void RefreshTree(string selectNamespace = "")
    {

        IEnumerable<NsTreeItem> nodes = AllTranslation.ForParse().ToNsTree();
        CurrentTreeItems.Clear();
        foreach (NsTreeItem node in nodes)
        {
            CurrentTreeItems.Add(node);
        }

    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }
    internal void UpdateSummaryInfo()
    {
        SummaryInfo.Update(AllTranslation);
    }

    [RelayCommand]
    private void NewLanguage()
    {
        var dialog = new LanguagePrompt("New Language", "Enter the translation language name below.", AllTranslation)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            AddLanguage(dialog.ResponseText);
        }
    }




    [RelayCommand]
    private void NewItem()
    {
        string ns = SelectedNode?.Namespace ?? "";

        var dialog = new PromptDialog("New Translation", "Please enter an ID for the translation\nUse '.' to create hierarchical IDs.", ns)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            CreateNewItem(dialog.ResponseText);
        }
    }



    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void RenameItem()
    {
        var node = SelectedNode;
        if (node == null) return;

        var dialog = new PromptDialog("Rename: " + node.Name, "Enter the new name below.", node.Name)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            RenameItem(node, dialog.ResponseText);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteItem))]
    private void DeleteItem()
    {
        var node = SelectedNode;
        if (node != null)
        {
            DeleteItem(node);
        }
    }

    private bool CanRenameItem()
    {
        return SelectedNode != null;
    }

    private bool CanDeleteItem()
    {
        return SelectedNode != null;
    }
    internal void Search(string ns, bool alwaysPaging = false)
    {

        bool isPartial = false;
        List<TranslationItem> matchedTranslations = AllTranslation.ToList();
        List<TranslationItem> translationItems;


        int TranslationCount = 0;
        if (string.IsNullOrWhiteSpace(ns))
        {
            translationItems = matchedTranslations.ToList();
        }
        else if (!ns.EndsWith(".", StringComparison.InvariantCultureIgnoreCase))
        {
            translationItems = matchedTranslations.Where(o => o.Namespace == ns).ToList();
        }
        else
        {
            List<TranslationItem> translations = matchedTranslations.Where(o => o.Namespace.StartsWith(ns, StringComparison.InvariantCulture)).ToList();

            if (!alwaysPaging && (translations.Count / 3 > AppOptions.TruncateResultsOver))
            {
                isPartial = true;
                translationItems = translations.Take(AppOptions.TruncateResultsOver).ToList();
                TranslationCount = translations.Count;
            }
            else
                translationItems = translations.ToList();
        }

        List<string> namespaces = translationItems.ToNamespaces().ToList();
        List<string> languages = AllTranslation.ToLanguages().ToList();


        List<LanguageGroupViewModel> languageGroups = new();
        foreach (string n in namespaces)
        {
            var languageGroupVm = new LanguageGroupViewModel(n);
            languageGroupVm.LoadTranslations(matchedTranslations.Where(o => o.Namespace == n).ToList());
            languageGroups.Add(languageGroupVm);
        }

        PagingController.SwapData(languageGroups, isPartial);
        PagedUpdates();
    }

    internal void ShowAll(string Path)
    {
        Search(Path, true);
    }
    [RelayCommand]
    internal void AddMissingTranslations()
    {
        List<string> namespaces = AllTranslation.ToNamespaces().ToList();
        List<string> allLanguages = AllTranslation.ToLanguages().ToList();

        foreach (string language in allLanguages)
        {
            IEnumerable<string> languageNamespaces = AllTranslation.OnlyLanguage(language).ToNamespaces();
            AllTranslation.AddRange(namespaces.Except(languageNamespaces).Select(o => new TranslationItem() { Namespace = o, Value = string.Empty, Language = language }));
        }

    }
    // NOTE: The [RelayCommand] above generates AddMissingTranslationsCommand

    // Fill empty translations — alias for pre-translate to keep behaviour explicit
    [RelayCommand]
    private async Task FillEmptyTranslations()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to fill.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        await _bulkActionService.PreTranslateAsync(AllTranslation).ConfigureAwait(true);
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = "Empty translations filled.";
    }

    [RelayCommand]
    private void DeleteUnusedTranslations()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded.");
            return;
        }

        if (!_messageService.ShowConfirmation("Delete all IDs that have no translation values for any language?"))
            return;

        var emptyNamespaces = AllTranslation.GroupBy(t => t.Namespace)
            .Where(g => g.All(i => string.IsNullOrEmpty(i.Value)))
            .Select(g => g.Key)
            .ToList();

        foreach (var ns in emptyNamespaces)
        {
            AllTranslation.RemoveAll(o => o.Namespace == ns);
        }

        RefreshTree();
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = $"Deleted {emptyNamespaces.Count} unused IDs";
    }

    partial void OnSelectedNodeChanged(NsTreeItem value)
    {
        try
        {
            (RenameItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (DeleteItemCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
        catch { }
    }

    [RelayCommand]
    private void ShowAll()
    {
        Search("", true);
    }

    #region File Menu


    [RelayCommand]
    private async Task NewFolder()
    {
        // Open the new project dialog to collect detailed settings
        NewProjectPrompt dialog;
        if (App.Services != null)
        {
            dialog = App.Services.GetService(typeof(NewProjectPrompt)) as NewProjectPrompt ?? new NewProjectPrompt("New Project", "Create a new project with languages and packages.", _projectService);
        }
        else
        {
            dialog = new NewProjectPrompt("New Project", "Create a new project with languages and packages.", _projectService);
        }
        dialog.Owner = Application.Current.MainWindow;

        // Show dialog and use the ViewModel to initialize project files when confirmed
        if (dialog.ShowDialog() == true && dialog.DataContext is NewProjectViewModel vm)
        {
            // require project folder
            if (string.IsNullOrWhiteSpace(vm.ProjectFolder))
            {
                _messageService.ShowMessage("No folder selected.");
                return;
            }

            try
            {
                // Project files already created by vm.CreateProject() in OKButton_Click
                CurrentPath = vm.ProjectFolder;
                AppOptions.DefaultPath = CurrentPath;
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
            AppOptions.DefaultPath = selected;
            _recentFileService.Add(selected);
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
        }
        else
        {
            _messageService.ShowMessage("No folder selected.");
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
        AppOptions.DefaultPath = directory;
        _recentFileService.Add(directory);
        await LoadFolderAsync(directory).ConfigureAwait(true);
    }


    private async Task LoadFolderAsync(string path)
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading project...";

            var loaded = await Task.Run(() => _projectService.Load(path)).ConfigureAwait(true);
            AllTranslation = loaded;
            AddMissingTranslations();
            RefreshTree();
            UpdateSummaryInfo();
            IsDirty = true;
            // Populate paging controller for loaded data
            Search("", true);
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
        IsDirty = false;
        switch (AppOptions.SaveStyle)
        {
            case SaveStyles.Json:
                if (_projectService != null)
                    _projectService.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation);
                break;
            case SaveStyles.Namespaced:
                if (_projectService != null)
                    _projectService.Save(CurrentPath, SaveStyles.Namespaced, CurrentTreeItems.ToList(), AllTranslation);
                break;
        }
    }

    private bool CanSave()
    {
        return IsDirty;
    }

    [RelayCommand]
    private void SaveTo()
    {
        VistaFolderBrowserDialog dialog = new()
        {
            SelectedPath = CurrentPath
        };
        bool? selected = dialog.ShowDialog(Application.Current.MainWindow);
        if (selected.GetValueOrDefault())
        {
            CurrentPath = dialog.SelectedPath;
            Save();
        }

    }
    [RelayCommand]
    private void CloseProject()
    {
        // Reset all project-related data and UI state
        AllTranslation = new();
        CurrentPath = string.Empty;
        SelectedNode = null!; // Intentional null to clear UI selection (null-forgiving)
        IsDirty = false;
        StatusText = string.Empty;

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
        var recents = _recentFileService.LoadRecent();
        if (recents.Count == 0)
        {
            _messageService.ShowMessage("No recent projects found.");
            return;
        }

        string path = recents?.First().Path; // For now, just use first. Replace with proper recent list picker later
        if (!Directory.Exists(path))
        {
            _messageService.ShowMessage($"Path not found: {path}");
            return;
        }

        CurrentPath = path;
        await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
    }



    [RelayCommand]
    private void ShowPreferences()
    {
        OptionDialog optionsHwnd = new(AppOptions) { Owner = Application.Current.MainWindow };
        bool? saved = optionsHwnd.ShowDialog();
        if (saved.GetValueOrDefault())
        {
            AppOptions = optionsHwnd.Config;
        }
        // Recreate or update the paging controller to reflect new options (page size and max items)
        int oldPage = PagingController?.Page ?? 1;
        int maxItems = AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;
        int pageSize = AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
        var currentData = PagingController?.Data ?? new System.Collections.ObjectModel.ObservableCollection<LanguageGroupViewModel>();
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, currentData, maxItems);
        PagingController.Page = Math.Min(Math.Max(1, oldPage), PagingController.Pages);
        PagedUpdates();

    }
    #endregion

    public void CreateNewItem(string newNamespace)
    {
        if (string.IsNullOrWhiteSpace(newNamespace))
            return;

        if (AllTranslation.NoEmpty().Any(setting => setting.Namespace.Contains(newNamespace)))
        {
            MessageBox.Show("Duplicate name");
            return;
        }

        var languages = AllTranslation.ToLanguages().ToList();
        foreach (string lang in languages)
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNamespace,
                Value = string.Empty,
                Language = lang
            });
        }

        RefreshTree(newNamespace);
        UpdateSummaryInfo();
        Search(newNamespace, true);
    }

    public void AddLanguage(string newLanguage)
    {
        if (string.IsNullOrWhiteSpace(newLanguage))
            return;

        if (AllTranslation.Any(setting => setting.Language == newLanguage))
        {
            MessageBox.Show("Duplicate language");
            return;
        }

        AllTranslation.Add(new TranslationItem
        {
            Namespace = "",
            Value = "",
            Language = newLanguage
        });

        AddMissingTranslations();
        UpdateSummaryInfo();
        RefreshTree();
        Search("", true);
        // Ensure the UI paging is refreshed after adding translations
        Search("", true);
    }

    public void RenameItem(NsTreeItem node, string newName)
    {
        if (node == null || string.IsNullOrWhiteSpace(newName) || newName.Contains('.'))
            return;

        string oldNs = node.Namespace;
        string newNs = oldNs[..oldNs.LastIndexOf(node.Name, StringComparison.InvariantCulture)] + newName.Trim();

        AllTranslation.ForParse().ToList().ForEach(item =>
        {
            if (item.Namespace.StartsWith(oldNs, StringComparison.InvariantCulture))
            {
                item.Namespace = item.Namespace.Replace(oldNs, newNs, StringComparison.InvariantCulture);
            }
        });

        RefreshTree(newNs);
        Search(newNs, true);
    }

    public void DeleteItem(NsTreeItem node)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.Namespace))
            return;

        if (node.Parent == null)
        {
            CurrentTreeItems.Remove(node);
        }
        else if (node.Parent.Items is List<NsTreeItem> siblings)
        {
            siblings.Remove(node);
        }

        AllTranslation.RemoveAll(o => o?.Namespace?.StartsWith(node.Namespace) ?? false);
        RefreshTree();
        Search("", true);
    }


}

