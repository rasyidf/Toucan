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
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Services;
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
    private ObservableCollection<NsTreeItem> selectedNodes = new();


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
    private readonly System.Func<string, LanguageGroupViewModel> _languageGroupFactory;
    private readonly System.Func<System.Collections.Generic.IEnumerable<string>, System.Collections.Generic.IEnumerable<TranslationItem>, Toucan.Core.Contracts.Services.IPretranslationService, PreTranslateViewModel> _preTranslateFactory;
    private readonly IProjectService _projectService;
    private readonly ITranslationStrategyFactory _strategyFactory;
    private readonly System.Func<NewProjectPrompt> _newProjectPromptFactory;
    private readonly Toucan.Core.Contracts.IValidationPipeline? _validationPipeline;
    private readonly Toucan.Core.Contracts.ISourceCodeService? _sourceCodeService;
    private readonly Toucan.Core.Contracts.ITranslationAnalyzer? _translationAnalyzer;


    public MainWindowViewModel(
        IRecentProjectService recentFileService,
        IDialogService dialogService,
        IMessageService messageService,
        IPreferenceService preferenceService,
        IBulkActionService bulkActionService = null,
        Toucan.Core.Contracts.Services.IPretranslationService pretranslationService = null,
        IProjectService projectService = null,
        ITranslationStrategyFactory strategyFactory = null,
        System.Func<string, LanguageGroupViewModel> languageGroupFactory = null,
        System.Func<System.Collections.Generic.IEnumerable<string>, System.Collections.Generic.IEnumerable<TranslationItem>, Toucan.Core.Contracts.Services.IPretranslationService, PreTranslateViewModel> preTranslateFactory = null,
        Toucan.Core.Contracts.IValidationPipeline validationPipeline = null,
        Toucan.Core.Contracts.ISourceCodeService sourceCodeService = null,
        Toucan.Core.Contracts.ITranslationAnalyzer translationAnalyzer = null)
    {
        _recentFileService = recentFileService;
        _dialogService = dialogService;
        _messageService = messageService;
        _preferenceService = preferenceService;
        _bulkActionService = bulkActionService;
        _pretranslationService = pretranslationService;
        _projectService = projectService;
        _strategyFactory = strategyFactory;
        _newProjectPromptFactory = null;
        _languageGroupFactory = languageGroupFactory;
        _preTranslateFactory = preTranslateFactory;
        _validationPipeline = validationPipeline;
        _sourceCodeService = sourceCodeService;
        _translationAnalyzer = translationAnalyzer;

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
        _dialogService.ShowAbout();
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
        try
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

                var vm = _preTranslateFactory != null ? _preTranslateFactory(languages, AllTranslation, _pretranslationService) : new PreTranslateViewModel(languages, AllTranslation, _pretranslationService);
                bool result = _dialogService.ShowPreTranslate(vm);

                if (result)
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

            // Ensure continuation runs on the UI context so we can update UI-bound properties safely
            await _bulkActionService.PreTranslateAsync(AllTranslation).ConfigureAwait(true);
            UpdateSummaryInfo();
            IsDirty = true;
            StatusText = "Pre-translation completed.";
        }
        catch (Exception ex)
        {
            // Catch any unexpected exception in the UI flow and surface a friendly message instead of crashing
            try
            {
                _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
            }
            catch
            {
                // if message service fails, swallow — no direct UI calls
            }
        }
    }

    [RelayCommand]
    private void GenerateStatisticsBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to generate statistics.");
            return;
        }

        var dialog = new Views.Dialogs.StatisticsDialog(AllTranslation, System.Windows.Application.Current?.MainWindow);
        dialog.ShowDialog();
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

            var ns = _dialogService.ShowPrompt("Pre-translate namespace", "Enter namespace (exact or prefix) to pre-translate for language: " + item.Language, "");
            if (ns == null) return;
            ns = ns.Trim();
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

            var key = _dialogService.ShowPrompt("Pre-translate key", "Enter exact key namespace/id to pre-translate for language: " + item.Language, "");
            if (key == null) return;
            key = key.Trim();
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
        IEnumerable<NsTreeItem> nodes = AppOptions?.PlainTextKeys == true
            ? Toucan.Extensions.TranslationItemExtensions.ToNsTreeFlat(AllTranslation.ForParse())
            : AllTranslation.ForParse().ToNsTree();
        CurrentTreeItems.Clear();
        foreach (NsTreeItem node in nodes)
        {
            CurrentTreeItems.Add(node);
        }
    }

    [RelayCommand]
    private void Exit()
    {
        _dialogService.Shutdown();
    }
    internal void UpdateSummaryInfo()
    {
        SummaryInfo.Update(AllTranslation);
        // show number of missing translations as a notification badge
        try
        {
            var totalMissing = SummaryInfo.Details?.Sum(d => (int)d.Missing) ?? 0;
            StatusBarService.Instance.ShowNotificationBadge(totalMissing);
        }
        catch { }
    }

    [RelayCommand]
    private void NewLanguage()
    {
        var result = _dialogService.ShowLanguagePrompt("New Language", "Enter the translation language name below.", AllTranslation);
        if (result != null)
        {
            AddLanguage(result);
        }
    }




    [RelayCommand]
    private void NewItem()
    {
        string ns = SelectedNode?.Namespace ?? "";
        var result = _dialogService.ShowPrompt("New Translation", "Please enter an ID for the translation\nUse '.' to create hierarchical IDs.", ns);
        if (result != null)
        {
            CreateNewItem(result);
        }
    }



    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void RenameItem()
    {
        var node = SelectedNode;
        if (node == null) return;

        var result = _dialogService.ShowPrompt("Rename: " + node.Name, "Enter the new name below.", node.Name);
        if (result != null)
        {
            RenameItem(node, result);
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

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void DuplicateItem()
    {
        var node = SelectedNode;
        if (node == null) return;

        var newNs = node.Namespace + "_copy";
        var existing = AllTranslation.Where(t => t.Namespace == node.Namespace).ToList();
        foreach (var item in existing)
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNs,
                Value = item.Value,
                Language = item.Language,
                Comment = item.Comment,
                IsApproved = false
            });
        }
        RefreshTree(newNs);
        UpdateSummaryInfo();
        Search(newNs, true);
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate1()
    {
        if (SelectedNode == null) return;
        var template = AppOptions?.CopyTemplate1 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate2()
    {
        if (SelectedNode == null) return;
        var template = AppOptions?.CopyTemplate2 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
    }

    [RelayCommand(CanExecute = nameof(CanRenameItem))]
    private void CopyAsTemplate3()
    {
        if (SelectedNode == null) return;
        var template = AppOptions?.CopyTemplate3 ?? "%1";
        System.Windows.Clipboard.SetText(template.Replace("%1", SelectedNode.Namespace));
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
            var languageGroupVm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
            languageGroupVm.LoadTranslations(matchedTranslations.Where(o => o.Namespace == n).ToList());
            languageGroups.Add(languageGroupVm);
        }

        PagingController.SwapData(languageGroups, isPartial);
        PagedUpdates();
    }

    partial void OnSearchTextChanged(string value)
    {
        // keep existing behaviour: search on text changes
        Search(value, false);
    }

    internal void ShowAll(string Path)
    {
        Search(Path, true);
    }

    [RelayCommand]
    private void FindPrompt()
    {
        var result = _dialogService.ShowPrompt("Find", "Enter text or namespace to find (append '.' to search by prefix)", SearchText ?? "");
        if (result != null)
        {
            SearchText = result.Trim();
            RecordFilterHistory(SearchText);
        }
    }

    [RelayCommand]
    private void FindNext()
    {
        NextPage();
    }

    [RelayCommand]
    private void SetFilter()
    {
        // equivalent to the find prompt - choose a filter to apply
        FindPrompt();
    }

    [RelayCommand]
    private void FilterById()
    {
        var result = _dialogService.ShowPrompt("Filter by ID", "Enter exact ID/namespace to filter by (exact match)", "");
        if (result != null)
        {
            SearchText = result.Trim();
        }
    }

    [RelayCommand]
    private void ShowUntranslated()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded.");
            return;
        }

        var matchedTranslations = AllTranslation.Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList();

        if (matchedTranslations.Count == 0)
        {
            _messageService.ShowMessage("No untranslated items found.");
            return;
        }

        List<string> namespaces = matchedTranslations.ToNamespaces().ToList();

        List<LanguageGroupViewModel> languageGroups = new();
        foreach (string n in namespaces)
        {
            var languageGroupVm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
            languageGroupVm.LoadTranslations(matchedTranslations.Where(o => o.Namespace == n).ToList());
            languageGroups.Add(languageGroupVm);
        }

        PagingController.SwapData(languageGroups, false);
        PagedUpdates();
    }

    [RelayCommand]
    private void ShowTranslated()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) return;
        var matched = AllTranslation.Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToList();
        FilterAndDisplay(matched, "No translated items found.");
    }

    [RelayCommand]
    private void ShowApproved()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) return;
        var matched = AllTranslation.Where(t => t.IsApproved).ToList();
        FilterAndDisplay(matched, "No approved items found.");
    }

    private void FilterAndDisplay(List<TranslationItem> matched, string emptyMessage)
    {
        if (matched.Count == 0)
        {
            _messageService.ShowMessage(emptyMessage);
            return;
        }
        var namespaces = matched.ToNamespaces().ToList();
        var groups = namespaces.Select(n =>
        {
            var vm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
            vm.LoadTranslations(matched.Where(o => o.Namespace == n).ToList());
            return vm;
        }).ToList();
        PagingController.SwapData(groups, false);
        PagedUpdates();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = string.Empty;
        Search("", true);
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
        // update statusbar cursor (selection info)
        if (value != null)
        {
            StatusBarService.Instance.UpdateCursor("Selected: " + value.Namespace);
        }
        else
        {
            StatusBarService.Instance.UpdateCursor("Ln 0, Col 0");
        }
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
            if (result == null) return;

            CurrentPath = result.Value.Folder;
            AppOptions.LastProjectPath = CurrentPath;
            _recentFileService.Add(CurrentPath);
            await LoadFolderAsync(CurrentPath).ConfigureAwait(true);
        }
    }

    // --- Source Code Integration ---

    [ObservableProperty]
    private bool sourceCodeScanned;

    [ObservableProperty]
    private string sourceCodeStatus = string.Empty;

    [RelayCommand]
    private async Task ScanSourceCode()
    {
        if (_sourceCodeService == null || string.IsNullOrEmpty(CurrentPath)) return;

        var settings = ProjectSettings.LoadFrom(CurrentPath);
        var roots = settings?.SourceRoots ?? [];
        // Default: scan project folder itself if no source roots configured
        var scanPath = roots.Count > 0
            ? Path.Combine(CurrentPath, roots[0])
            : CurrentPath;

        StatusText = "Scanning source code...";
        var result = await _sourceCodeService.ScanAsync(scanPath).ConfigureAwait(true);
        SourceCodeScanned = true;
        SourceCodeStatus = $"{result.KeysFound} keys found in {result.FilesScanned} files ({result.Duration.TotalSeconds:F1}s)";
        StatusText = SourceCodeStatus;
    }

    [RelayCommand]
    private void FilterUsedKeys()
    {
        if (_sourceCodeService == null || !_sourceCodeService.HasScanData || AllTranslation == null) return;
        var allKeys = AllTranslation.Select(t => t.Namespace).Distinct();
        var unused = _sourceCodeService.GetUnusedKeys(allKeys).ToHashSet();
        // Filter to show only USED keys
        Search("", true); // reset first
        FilteredBySourceUsage = "used";
        OnPropertyChanged(nameof(FilteredBySourceUsage));
    }

    [RelayCommand]
    private void FilterUnusedKeys()
    {
        if (_sourceCodeService == null || !_sourceCodeService.HasScanData || AllTranslation == null) return;
        FilteredBySourceUsage = "unused";
        OnPropertyChanged(nameof(FilteredBySourceUsage));
    }

    [RelayCommand]
    private void ClearSourceFilter()
    {
        FilteredBySourceUsage = null;
        Search("", true);
    }

    [RelayCommand]
    private void OpenInEditor(string key)
    {
        if (_sourceCodeService == null) return;
        var usages = _sourceCodeService.FindUsages(key).ToList();
        if (usages.Count == 0) return;

        var first = usages[0];
        var settings = ProjectSettings.LoadFrom(CurrentPath);
        var editor = settings?.ExternalEditor ?? "code --goto \"{file}:{line}\"";
        var fullPath = Path.Combine(CurrentPath, first.FilePath);

        var cmd = editor.Replace("{file}", fullPath).Replace("{line}", first.Line.ToString());
        try { Process.Start(new ProcessStartInfo("cmd", $"/c {cmd}") { CreateNoWindow = true }); }
        catch { /* non-critical */ }
    }

    /// <summary>Current source usage filter: null = off, "used", "unused".</summary>
    public string? FilteredBySourceUsage { get; private set; }

    /// <summary>Get source code usages for the selected key.</summary>
    public IEnumerable<Toucan.Core.Contracts.KeyUsage> GetKeyUsages(string key)
        => _sourceCodeService?.FindUsages(key) ?? [];

    // --- Translation Quality Analysis ---

    public ObservableCollection<Toucan.Core.Contracts.AnalysisResult> AnalysisResults { get; } = new();

    [RelayCommand]
    private async Task AnalyzeTranslations()
    {
        if (_translationAnalyzer == null || AllTranslation == null || AllTranslation.Count == 0) return;

        var settings = ProjectSettings.LoadFrom(CurrentPath);
        var primaryLang = settings?.PrimaryLanguage ?? "en-US";
        var appContext = AppOptions.Context;

        if (string.IsNullOrWhiteSpace(appContext))
        {
            appContext = _dialogService.ShowPrompt("Application Context",
                "Describe your application domain (e.g., 'Banking app for managing savings accounts and transactions').\nThis helps the analyzer check domain-specific terminology.",
                appContext ?? "");
            if (string.IsNullOrWhiteSpace(appContext)) return;
            AppOptions.Context = appContext;
            _preferenceService.Save(AppOptions);
        }

        // Build analysis items: pair source + each translation
        var sourceMap = AllTranslation
            .Where(i => i.Language == primaryLang && !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, i => i.Value);

        var items = AllTranslation
            .Where(i => i.Language != primaryLang && !string.IsNullOrEmpty(i.Value))
            .Where(i => sourceMap.ContainsKey(i.Namespace))
            .Select(i => new Toucan.Core.Contracts.AnalysisItem(i.Namespace, sourceMap[i.Namespace], i.Value, i.Language))
            .ToList();

        if (items.Count == 0) { _messageService.ShowMessage("No translations to analyze."); return; }

        // Build provider options from saved settings
        var providerOpts = new Dictionary<string, string>();
        var settingsService = App.Services?.GetService(typeof(Toucan.Services.IProviderSettingsService)) as Toucan.Services.IProviderSettingsService;
        if (settingsService != null)
        {
            var all = settingsService.LoadAppProviderSettings();
            var match = all.FirstOrDefault(p => string.Equals(p.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                foreach (var kv in match.Options) providerOpts[kv.Key] = kv.Value;
                foreach (var kv in match.Secrets) providerOpts[kv.Key] = kv.Value;
            }
        }

        StatusText = "Analyzing translations...";
        var request = new Toucan.Core.Contracts.AnalysisRequest
        {
            Items = items,
            ApplicationContext = appContext,
            SourceLanguage = primaryLang,
            ProviderOptions = providerOpts
        };

        var progress = new Progress<PretranslationProgress>(p => StatusText = p.Message ?? $"Analyzing {p.Completed}/{p.Total}...");
        var results = await _translationAnalyzer.AnalyzeAsync(request, progress).ConfigureAwait(true);

        AnalysisResults.Clear();
        foreach (var r in results.Where(r => r.Confidence >= 0.6))
            AnalysisResults.Add(r);

        StatusText = AnalysisResults.Count > 0
            ? $"Analysis complete: {AnalysisResults.Count} issue(s) found"
            : "Analysis complete: no issues found";
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
                        var root = Newtonsoft.Json.Linq.JObject.Parse(txt);
                        var primary = root["primaryLanguage"]?.ToString();
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
                        StatusBarService.Instance.UpdateDefaultLanguage(firstLang);
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
            var ctx = new Toucan.Core.Contracts.ValidationContext
            {
                Items = AllTranslation,
                Settings = ProjectSettings.LoadFrom(CurrentPath) ?? ProjectSettings.CreateDefault(CurrentPath)
            };
            var errors = _validationPipeline.RunAll(ctx)
                .Where(r => r.Severity == Toucan.Core.Contracts.ValidationSeverity.Error)
                .ToList();

            if (errors.Count > 0)
            {
                var msg = $"{errors.Count} validation error(s) found:\n" +
                    string.Join("\n", errors.Take(5).Select(e => $"• [{e.Language}] {e.Namespace}: {e.Message}"));
                if (errors.Count > 5) msg += $"\n... and {errors.Count - 5} more";
                if (!_messageService.ShowConfirmation(msg + "\n\nSave anyway?", "Validation Errors"))
                    return;
            }
        }

        IsDirty = false;
        _projectService?.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation);
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
        AllTranslation = new();
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
    private ObservableCollection<Project> recentProjects = new();

    internal void RefreshRecentProjects()
    {
        var list = _recentFileService.LoadRecent();
        RecentProjects.Clear();
        foreach (var p in list)
            RecentProjects.Add(p);
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
            _recentFileService.Remove(p.Path);
        RecentProjects.Clear();
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", CurrentPath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowPreferences()
    {
        if (_dialogService.ShowOptions(AppOptions, CurrentPath, out var updated) && updated != null)
        {
            AppOptions = updated;
            try { StatusBarService.Instance.UpdateDefaultLanguage(AppOptions.DefaultLanguage ?? "en-US"); } catch { }
        }
        // Recreate paging controller with new options
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
            _messageService.ShowMessage("Duplicate name");
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

        // Auto-select the newly created node
        var newNode = CurrentTreeItems.SelectMany(FindAll).FirstOrDefault(n => n.Namespace == newNamespace);
        if (newNode != null) SelectedNode = newNode;
    }

    [RelayCommand]
    private void ToggleNodeSelection(NsTreeItem node)
    {
        if (node == null) return;
        node.IsSelected = !node.IsSelected;
        if (node.IsSelected)
            SelectedNodes.Add(node);
        else
            SelectedNodes.Remove(node);
    }

    [RelayCommand]
    private void DeleteSelectedItems()
    {
        if (SelectedNodes.Count == 0) return;
        if (!_messageService.ShowConfirmation($"Delete {SelectedNodes.Count} selected items?")) return;
        foreach (var node in SelectedNodes.ToList())
        {
            AllTranslation.RemoveAll(o => o.Namespace != null && o.Namespace.StartsWith(node.Namespace));
        }
        SelectedNodes.Clear();
        RefreshTree();
        UpdateSummaryInfo();
        Search("", true);
        IsDirty = true;
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedNodes.Clear();
        foreach (var node in CurrentTreeItems.SelectMany(FindAll))
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var node in SelectedNodes)
            node.IsSelected = false;
        SelectedNodes.Clear();
    }

    private static IEnumerable<NsTreeItem> FindAll(NsTreeItem node)
    {
        yield return node;
        if (node.Items != null)
            foreach (var child in node.Items.SelectMany(FindAll))
                yield return child;
    }

    public void AddLanguage(string newLanguage)
    {
        if (string.IsNullOrWhiteSpace(newLanguage))
            return;

        if (AllTranslation.Any(setting => setting.Language == newLanguage))
        {
            _messageService.ShowMessage("Duplicate language");
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

    #region Filter History

    [ObservableProperty]
    private ObservableCollection<string> filterHistory = new();

    private void RecordFilterHistory(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        FilterHistory.Remove(filter); // dedupe
        FilterHistory.Insert(0, filter);
        while (FilterHistory.Count > 15) FilterHistory.RemoveAt(FilterHistory.Count - 1);

        // Persist
        AppOptions.FilterHistory = FilterHistory.ToList();
        AppOptions.ToDisk();
    }

    internal void LoadFilterHistory()
    {
        FilterHistory.Clear();
        foreach (var f in AppOptions?.FilterHistory ?? [])
            FilterHistory.Add(f);
    }

    #endregion

    #region Undo / Redo

    [RelayCommand]
    private void Undo()
    {
        var action = Services.UndoRedoService.Instance.Undo();
        if (action == null) return;
        ApplyUndoRedo(action.Namespace, action.Language, action.OldValue);
    }

    [RelayCommand]
    private void Redo()
    {
        var action = Services.UndoRedoService.Instance.Redo();
        if (action == null) return;
        ApplyUndoRedo(action.Namespace, action.Language, action.NewValue);
    }

    private void ApplyUndoRedo(string ns, string language, string value)
    {
        var item = AllTranslation?.FirstOrDefault(t => t.Namespace == ns && t.Language == language);
        if (item == null) return;
        item.Value = value;
        IsDirty = true;
        // Refresh UI if currently viewing this namespace
        Search(SearchText ?? "", true);
    }

    #endregion

    #region Edit Actions (Convert Case, Trim, Clipboard)

    private void ApplyToAllValues(Func<string, string> transform)
    {
        if (AllTranslation == null || AllTranslation.Count == 0) return;
        foreach (var item in AllTranslation.Where(t => !string.IsNullOrEmpty(t.Value)))
            item.Value = transform(item.Value);
        IsDirty = true;
        Search(SearchText ?? "", true);
    }

    [RelayCommand] private void ConvertLowercase() => ApplyToAllValues(v => v.ToLowerInvariant());
    [RelayCommand] private void ConvertUppercase() => ApplyToAllValues(v => v.ToUpperInvariant());
    [RelayCommand] private void ConvertSentenceCase() => ApplyToAllValues(v => v.Length > 0 ? char.ToUpper(v[0]) + v[1..].ToLowerInvariant() : v);
    [RelayCommand] private void ConvertTitleCase() => ApplyToAllValues(v => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(v.ToLowerInvariant()));

    [RelayCommand] private void TrimWhitespace() => ApplyToAllValues(v => v.Trim());
    [RelayCommand] private void TrimLineByLine() => ApplyToAllValues(v => string.Join("\n", v.Split('\n').Select(l => l.Trim())));
    [RelayCommand] private void SimplifyWhitespace() => ApplyToAllValues(v => System.Text.RegularExpressions.Regex.Replace(v.Trim(), @"\s+", " "));

    [RelayCommand]
    private void EditCut()
    {
        if (SelectedNode == null) return;
        var items = AllTranslation.Where(t => t.Namespace == SelectedNode.Namespace).ToList();
        if (items.Count == 0) return;
        var text = string.Join("\n", items.Select(t => $"{t.Language}={t.Value}"));
        System.Windows.Clipboard.SetText(text);
        DeleteItem(SelectedNode);
    }

    [RelayCommand]
    private void EditCopy()
    {
        if (SelectedNode == null) return;
        var items = AllTranslation.Where(t => t.Namespace == SelectedNode.Namespace).ToList();
        if (items.Count == 0) return;
        var text = string.Join("\n", items.Select(t => $"{t.Language}={t.Value}"));
        System.Windows.Clipboard.SetText(text);
    }

    [RelayCommand]
    private void EditPaste()
    {
        if (!System.Windows.Clipboard.ContainsText()) return;
        var text = System.Windows.Clipboard.GetText();
        // Parse lines in format "language=value" and apply to current selected node
        if (SelectedNode == null) return;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var lang = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            var existing = AllTranslation.FirstOrDefault(t => t.Namespace == SelectedNode.Namespace && t.Language == lang);
            if (existing != null) existing.Value = val;
        }
        IsDirty = true;
        Search(SelectedNode.Namespace, true);
    }

    #endregion

    #region View Menu

    [RelayCommand]
    private void SwitchToTreeView()
    {
        IsTreeView = true;
    }

    [RelayCommand]
    private void SwitchToListView()
    {
        IsTreeView = false;
    }

    [ObservableProperty]
    private bool showMachineTranslations;

    [RelayCommand]
    private void ToggleShowMachineTranslations()
    {
        ShowMachineTranslations = !ShowMachineTranslations;
        // ponytail: placeholder toggle — visual indicator only for now
        // upgrade path: filter/highlight machine-translated values when metadata is tracked
        StatusText = ShowMachineTranslations ? "Showing machine translations" : "Machine translations hidden";
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

    private static SaveStyles FilterIndexToStyle(int index) => index switch
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

    [RelayCommand]
    private void Import()
    {
        var file = _dialogService.SelectFile(CurrentPath, ImportExportFilter);
        if (string.IsNullOrEmpty(file)) return;

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

            var imported = loader.Load(folder).ToList();
            if (imported.Count == 0)
            {
                _messageService.ShowMessage("No translations found in the selected file.");
                return;
            }

            // Merge imported items into current project
            AllTranslation ??= new List<TranslationItem>();
            foreach (var item in imported)
            {
                var existing = AllTranslation.FirstOrDefault(t => t.Namespace == item.Namespace && t.Language == item.Language);
                if (existing != null)
                    existing.Value = item.Value;
                else
                    AllTranslation.Add(item);
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
        if (string.IsNullOrEmpty(folder)) return;

        // Ask user for format via a simple prompt listing options
        var formatInput = _dialogService.ShowPrompt("Export Format",
            "Enter format: json, yaml, toml, xml, strings, xliff, arb, csv, resx, po, ini",
            "json");
        if (string.IsNullOrWhiteSpace(formatInput)) return;

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
        if (string.IsNullOrEmpty(path)) return;

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
        if (string.IsNullOrEmpty(file)) return;

        try
        {
            var imported = Toucan.Core.Services.ExcelService.Import(file);
            if (imported.Count == 0)
            {
                _messageService.ShowMessage("No translations found in the Excel file.");
                return;
            }

            AllTranslation ??= new List<TranslationItem>();
            foreach (var item in imported)
            {
                var existing = AllTranslation.FirstOrDefault(t => t.Namespace == item.Namespace && t.Language == item.Language);
                if (existing != null) existing.Value = item.Value;
                else AllTranslation.Add(item);
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

    #region Panels & Editor Modes

    [ObservableProperty]
    private bool suggestionsVisible;

    [ObservableProperty]
    private ObservableCollection<string> suggestions = new();

    [ObservableProperty]
    private bool focusedEditorMode;

    [ObservableProperty]
    private int focusedIndex;

    [ObservableProperty]
    private bool zenMode;

    [ObservableProperty]
    private bool infiniteScroll;

    /// <summary>Languages selected for Focused Editor mode. Null/empty = show all.</summary>
    [ObservableProperty]
    private ObservableCollection<string> focusedLanguages = new();

    [RelayCommand]
    private void SelectFocusedLanguages()
    {
        if (AllTranslation == null) return;
        var allLangs = AllTranslation.ToLanguages().ToList();
        var input = _dialogService.ShowPrompt("Focused Languages",
            $"Enter language codes to show (comma-separated), or leave empty for all.\nAvailable: {string.Join(", ", allLangs)}",
            string.Join(", ", FocusedLanguages));
        if (input == null) return;

        FocusedLanguages.Clear();
        foreach (var lang in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
                FocusedLanguages.Add(lang);
        }
    }

    [RelayCommand]
    private void ToggleSuggestions()
    {
        SuggestionsVisible = !SuggestionsVisible;
        if (SuggestionsVisible)
            RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (AllTranslation == null || SelectedNode == null) return;

        // ponytail: naive fuzzy match — find translations with similar namespace or value
        var current = AllTranslation.FirstOrDefault(t => t.Namespace == SelectedNode.Namespace);
        if (current == null || string.IsNullOrWhiteSpace(current.Value)) return;

        var similar = AllTranslation
            .Where(t => t.Language == current.Language && t.Namespace != current.Namespace && !string.IsNullOrWhiteSpace(t.Value))
            .Where(t => t.Value.Contains(current.Value[..Math.Min(4, current.Value.Length)], StringComparison.OrdinalIgnoreCase)
                     || current.Value.Contains(t.Value[..Math.Min(4, t.Value.Length)], StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(t => $"{t.Namespace}: {t.Value}");

        foreach (var s in similar)
            Suggestions.Add(s);
    }

    [RelayCommand]
    private void ToggleFocusedEditor()
    {
        FocusedEditorMode = !FocusedEditorMode;
        if (FocusedEditorMode)
            FocusedIndex = 0;
    }

    [RelayCommand]
    private void FocusedNext()
    {
        if (PagingController == null) return;
        var max = PagingController.Data?.Count ?? 0;
        if (FocusedIndex < max - 1) FocusedIndex++;
    }

    [RelayCommand]
    private void FocusedPrevious()
    {
        if (FocusedIndex > 0) FocusedIndex--;
    }

    [RelayCommand]
    private void ToggleZenMode()
    {
        ZenMode = !ZenMode;
        Services.PanelService.Instance.ToggleZenMode();
    }

    [RelayCommand]
    private void ZenNext()
    {
        if (!ZenMode && !FocusedEditorMode) return;
        FocusedNext();
    }

    [RelayCommand]
    private void ZenPrevious()
    {
        if (!ZenMode && !FocusedEditorMode) return;
        FocusedPrevious();
    }

    [RelayCommand]
    private void ToggleInfiniteScroll()
    {
        InfiniteScroll = !InfiniteScroll;
        if (InfiniteScroll)
        {
            // Show all items without pagination
            PagingController.SwapData(PagingController.Data?.ToList() ?? new List<LanguageGroupViewModel>(), false);
            PagingController.UpdatePageSize(int.MaxValue);
        }
        else
        {
            int pageSize = AppOptions?.PageSize <= 0 ? 30 : AppOptions.PageSize;
            PagingController.UpdatePageSize(pageSize);
        }
        PagedUpdates();
    }

    #endregion

    #region Plural / Gender / Placeholder

    [RelayCommand]
    private void GeneratePluralForms()
    {
        if (SelectedNode == null || AllTranslation == null) return;
        var baseKey = PluralService.GetBaseKey(SelectedNode.Namespace);
        var languages = AllTranslation.ToLanguages().ToList();
        var added = 0;
        foreach (var lang in languages)
        {
            var missing = PluralService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            added += missing.Count;
        }
        if (added > 0) { RefreshTree(); UpdateSummaryInfo(); IsDirty = true; }
        StatusText = added > 0 ? $"Generated {added} plural forms for '{baseKey}'" : "All plural forms already exist.";
    }

    [RelayCommand]
    private void GenerateGenderForms()
    {
        if (SelectedNode == null || AllTranslation == null) return;
        var baseKey = GenderService.GetBaseKey(SelectedNode.Namespace);
        var languages = AllTranslation.ToLanguages().ToList();
        var added = 0;
        foreach (var lang in languages)
        {
            var missing = GenderService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            added += missing.Count;
        }
        if (added > 0) { RefreshTree(); UpdateSummaryInfo(); IsDirty = true; }
        StatusText = added > 0 ? $"Generated {added} gender forms for '{baseKey}'" : "All gender forms already exist.";
    }

    [RelayCommand]
    private void ValidatePlaceholders()
    {
        if (AllTranslation == null || AllTranslation.Count == 0) return;
        var languages = AllTranslation.ToLanguages().ToList();
        var primary = languages.FirstOrDefault() ?? "en";
        var issues = new List<string>();

        var sourceItems = AllTranslation.Where(t => t.Language == primary && !string.IsNullOrEmpty(t.Value)).ToList();
        foreach (var source in sourceItems)
        {
            foreach (var lang in languages.Where(l => l != primary))
            {
                var target = AllTranslation.FirstOrDefault(t => t.Namespace == source.Namespace && t.Language == lang);
                if (target == null || string.IsNullOrEmpty(target.Value)) continue;
                var result = PlaceholderService.Validate(source.Value, target.Value);
                if (!result.IsValid)
                    issues.Add($"{source.Namespace} [{lang}]: missing={string.Join(",", result.Missing)} extra={string.Join(",", result.Extra)}");
            }
        }

        if (issues.Count == 0)
            _messageService.ShowMessage("All placeholders are consistent across translations.");
        else
            _messageService.ShowMessage($"{issues.Count} placeholder issue(s):\n" + string.Join("\n", issues.Take(20)));
    }

    #endregion


}

