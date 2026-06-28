using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Extensions;
using Toucan.Services;

namespace Toucan.ViewModels;

/// <summary>
/// Navigation, filtering, pagination, view-mode toggling, filter history, and editor modes.
/// </summary>
internal partial class MainWindowViewModel
{
    /// <summary>
    /// Fuzzy search service for hybrid matching against namespaces and values.
    /// Set by DI registration (task 4.4).
    /// </summary>
    internal IFuzzySearchService? FuzzySearchService { get; set; }

    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;

    #region Search & Filter

    internal void Search(string ns, bool alwaysPaging = false)
    {
        if (FuzzySearchService == null)
        {
            // Fallback: show all items if service not available
            var allItems = AllTranslation.ToList();
            var allNs = allItems.ToNamespaces().ToList();
            List<LanguageGroupViewModel> groups = [];
            foreach (string n in allNs)
            {
                var vm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
                vm.LoadTranslations(allItems.Where(o => o.Namespace == n).ToList());
                groups.Add(vm);
            }
            PagingController.SwapData(groups, false);

            // Update status text with result count (fallback branch)
            if (!string.IsNullOrWhiteSpace(ns))
            {
                var totalItems = AllTranslation.Select(t => t.Namespace).Distinct().Count();
                StatusText = $"Showing {allNs.Count} of {totalItems} items";
            }
            else
            {
                var totalItems = AllTranslation.Select(t => t.Namespace).Distinct().Count();
                StatusText = $"{totalItems} items";
            }

            PagedUpdates();
            return;
        }

        var searchResults = FuzzySearchService.Search(AllTranslation, ns);

        // Get distinct namespaces from search results, preserving score order
        var matchedNamespaces = searchResults
            .Select(m => m.Item.Namespace)
            .Distinct()
            .ToList();

        bool isPartial = false;
        if (!alwaysPaging && matchedNamespaces.Count > AppOptions.TruncateResultsOver)
        {
            isPartial = true;
            matchedNamespaces = matchedNamespaces.Take(AppOptions.TruncateResultsOver).ToList();
        }

        List<LanguageGroupViewModel> languageGroups = [];
        foreach (string n in matchedNamespaces)
        {
            var languageGroupVm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
            languageGroupVm.LoadTranslations(AllTranslation.Where(o => o.Namespace == n).ToList());
            languageGroups.Add(languageGroupVm);
        }

        PagingController.SwapData(languageGroups, isPartial);

        // Update status text with result count
        if (!string.IsNullOrWhiteSpace(ns))
        {
            var totalItems = AllTranslation.Select(t => t.Namespace).Distinct().Count();
            StatusText = $"Showing {matchedNamespaces.Count} of {totalItems} items";
        }
        else
        {
            var totalItems = AllTranslation.Select(t => t.Namespace).Distinct().Count();
            StatusText = $"{totalItems} items";
        }

        PagedUpdates();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer.Stop();
            Search(value, false);
        };
        _searchDebounceTimer.Start();
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

        List<TranslationItem> matchedTranslations = AllTranslation.Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList();

        if (matchedTranslations.Count == 0)
        {
            _messageService.ShowMessage("No untranslated items found.");
            return;
        }

        List<string> namespaces = matchedTranslations.ToNamespaces().ToList();

        List<LanguageGroupViewModel> languageGroups = [];
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
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        List<TranslationItem> matched = AllTranslation.Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToList();
        FilterAndDisplay(matched, "No translated items found.");
    }

    [RelayCommand]
    private void ShowApproved()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        List<TranslationItem> matched = AllTranslation.Where(t => t.IsApproved).ToList();
        FilterAndDisplay(matched, "No approved items found.");
    }

    private void FilterAndDisplay(List<TranslationItem> matched, string emptyMessage)
    {
        if (matched.Count == 0)
        {
            _messageService.ShowMessage(emptyMessage);
            return;
        }
        List<string> namespaces = matched.ToNamespaces().ToList();
        List<LanguageGroupViewModel> groups = namespaces.Select(n =>
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
        ActiveLanguageFilter = null;
        SearchText = string.Empty;
        Search("", true);
    }

    [RelayCommand]
    private void ShowAll()
    {
        ActiveLanguageFilter = null;
        Search("", true);
    }

    #endregion

    #region Language Status Filtering

    /// <summary>
    /// Active language+status filter. Format: "status:languageCode" (e.g. "translated:en", "empty:fr").
    /// Null means no active language filter.
    /// </summary>
    [ObservableProperty]
    private string? activeLanguageFilter;

    /// <summary>
    /// Filters the translation list by language and status.
    /// Parameter format: "status:languageCode" where status is translated|empty|needsreview|approved.
    /// </summary>
    [RelayCommand]
    private void FilterByLanguageStatus(string? parameter)
    {
        if (string.IsNullOrEmpty(parameter))
        {
            return;
        }

        var colonIndex = parameter.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= parameter.Length - 1)
        {
            return;
        }

        var status = parameter[..colonIndex].ToLowerInvariant();
        var language = parameter[(colonIndex + 1)..];

        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        // Get items for this specific language
        var languageItems = AllTranslation
            .Where(t => t.Language == language && !string.IsNullOrWhiteSpace(t.Namespace))
            .ToList();

        List<TranslationItem> matched = status switch
        {
            "translated" => languageItems.Where(t => !string.IsNullOrEmpty(t.Value)).ToList(),
            "empty" => languageItems.Where(t => string.IsNullOrEmpty(t.Value)).ToList(),
            "needsreview" => languageItems.Where(t => !string.IsNullOrEmpty(t.Value) && !t.IsApproved).ToList(),
            "approved" => languageItems.Where(t => t.IsApproved).ToList(),
            _ => languageItems
        };

        ActiveLanguageFilter = parameter;
        StatusText = $"Filter: {status} ({language}) — {matched.Count} item(s)";

        if (matched.Count == 0)
        {
            PagingController.SwapData(new List<LanguageGroupViewModel>(), false);
            PagedUpdates();
            return;
        }

        // Build display groups from matched namespaces
        List<string> namespaces = matched.Select(t => t.Namespace).Distinct().OrderBy(n => n).ToList();
        List<LanguageGroupViewModel> groups = [];
        foreach (string n in namespaces)
        {
            var vm = _languageGroupFactory != null ? _languageGroupFactory(n) : new LanguageGroupViewModel(n);
            // Load all languages for this namespace so the editor shows full context
            vm.LoadTranslations(AllTranslation.Where(o => o.Namespace == n).ToList());
            groups.Add(vm);
        }

        PagingController.SwapData(groups, false);
        PagedUpdates();
    }

    #endregion

    #region Selected Node

    partial void OnSelectedNodeChanged(NsTreeItem? value)
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

    #endregion

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

    [RelayCommand]
    private void GoToPage(int? page)
    {
        if (PagingController == null)
        {
            return;
        }

        if (page == null)
        {
            return;
        }

        if (page < 1 || page > PagingController.Pages)
        {
            return;
        }

        PagingController.Page = page.Value;
        PagedUpdates();
    }

    #endregion

    #region View Mode

    [RelayCommand]
    private void ToggleViewMode()
    {
        IsTreeView = !IsTreeView;
    }

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

    #endregion

    #region Filter History

    [ObservableProperty]
    private ObservableCollection<string> filterHistory = [];

    private void RecordFilterHistory(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        _ = FilterHistory.Remove(filter); // dedupe
        FilterHistory.Insert(0, filter);
        while (FilterHistory.Count > 15)
        {
            FilterHistory.RemoveAt(FilterHistory.Count - 1);
        }

        // Persist
        AppOptions.FilterHistory = FilterHistory.ToList();
        AppOptions.ToDisk();
    }

    internal void LoadFilterHistory()
    {
        FilterHistory.Clear();
        foreach (var f in AppOptions?.FilterHistory ?? [])
        {
            FilterHistory.Add(f);
        }
    }

    #endregion

    #region Panels & Editor Modes

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
    private ObservableCollection<string> focusedLanguages = [];

    [RelayCommand]
    private void SelectFocusedLanguages()
    {
        if (AllTranslation == null)
        {
            return;
        }

        List<string> allLangs = AllTranslation.ToLanguages().ToList();
        var input = _dialogService.ShowPrompt("Focused Languages",
            $"Enter language codes to show (comma-separated), or leave empty for all.\nAvailable: {string.Join(", ", allLangs)}",
            string.Join(", ", FocusedLanguages));
        if (input == null)
        {
            return;
        }

        FocusedLanguages.Clear();
        foreach (var lang in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allLangs.Contains(lang, StringComparer.OrdinalIgnoreCase))
            {
                FocusedLanguages.Add(lang);
            }
        }
    }

    [RelayCommand]
    private void ToggleFocusedEditor()
    {
        FocusedEditorMode = !FocusedEditorMode;
        if (FocusedEditorMode)
        {
            FocusedIndex = 0;
        }
    }

    [RelayCommand]
    private void FocusedNext()
    {
        if (PagingController == null)
        {
            return;
        }

        var max = PagingController.Data?.Count ?? 0;
        if (FocusedIndex < max - 1)
        {
            FocusedIndex++;
        }
    }

    [RelayCommand]
    private void FocusedPrevious()
    {
        if (FocusedIndex > 0)
        {
            FocusedIndex--;
        }
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
        if (!ZenMode && !FocusedEditorMode)
        {
            return;
        }

        FocusedNext();
    }

    [RelayCommand]
    private void ZenPrevious()
    {
        if (!ZenMode && !FocusedEditorMode)
        {
            return;
        }

        FocusedPrevious();
    }

    [RelayCommand]
    private void ToggleInfiniteScroll()
    {
        InfiniteScroll = !InfiniteScroll;
        if (InfiniteScroll)
        {
            // Show all items without pagination
            PagingController.SwapData(PagingController.Data?.ToList() ?? [], false);
            PagingController.UpdatePageSize(int.MaxValue);
        }
        else
        {
            int pageSize = AppOptions?.PageSize <= 0 ? 30 : AppOptions!.PageSize; // Null-forgiving: condition above guarantees AppOptions is non-null
            PagingController.UpdatePageSize(pageSize);
        }
        PagedUpdates();
    }

    #endregion
}
