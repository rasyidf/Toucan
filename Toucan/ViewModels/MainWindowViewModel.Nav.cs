using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    #region Search & Filter

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
            {
                translationItems = translations.ToList();
            }
        }

        List<string> namespaces = translationItems.ToNamespaces().ToList();
        List<string> languages = AllTranslation.ToLanguages().ToList();


        List<LanguageGroupViewModel> languageGroups = [];
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
        SearchText = string.Empty;
        Search("", true);
    }

    [RelayCommand]
    private void ShowAll()
    {
        Search("", true);
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
