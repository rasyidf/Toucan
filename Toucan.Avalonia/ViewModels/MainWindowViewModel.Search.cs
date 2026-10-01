using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Find &amp; replace across translation values (Search panel).</summary>
public partial class MainWindowViewModel
{
    private const int MaxHistorySize = 20;

    /// <summary>Query for the Search panel, separate from the editor filter so typing doesn't re-filter the editor.</summary>
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private string replaceText = string.Empty;
    [ObservableProperty] private bool useRegex;
    [ObservableProperty] private bool showReplacePanel;
    [ObservableProperty] private int searchScopeIndex;
    [ObservableProperty] private string? searchLanguage;
    [ObservableProperty] private ObservableCollection<SearchResultItem> searchResults = [];
    [ObservableProperty] private ObservableCollection<string> searchHistory = [];
    [ObservableProperty] private SearchResultItem? selectedSearchResult;
    [ObservableProperty] private string searchStatus = string.Empty;

    public static IReadOnlyList<string> SearchScopes { get; } = ["All languages", "One language", "Selected key path"];

    private SearchScope CurrentScope => SearchScopeIndex switch
    {
        1 => SearchScope.SpecificLanguage,
        2 => SearchScope.CurrentNamespace,
        _ => SearchScope.AllLanguages
    };

    public bool IsLanguageScope => SearchScopeIndex == 1;

    partial void OnSearchScopeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsLanguageScope));
        if (value == 1 && string.IsNullOrEmpty(SearchLanguage)) SearchLanguage = PrimaryLanguage;
    }

    partial void OnSelectedSearchResultChanged(SearchResultItem? value)
    {
        if (value != null) RevealKey(value.Key);
    }

    [RelayCommand]
    private void OpenSearchPanel() => SidePanelRegistry.Instance.Activate("search");

    [RelayCommand]
    private void ExecuteSearch()
    {
        if (string.IsNullOrEmpty(SearchQuery))
        {
            SearchResults = [];
            SearchStatus = string.Empty;
            return;
        }

        FlushPendingEdits();
        string? scopeFilter = CurrentScope switch
        {
            SearchScope.SpecificLanguage => SearchLanguage,
            SearchScope.CurrentNamespace => SelectedNode?.Namespace ?? SelectedGroup?.Namespace,
            _ => null
        };

        try
        {
            var results = _searchAndReplace.Search(AllTranslation, SearchQuery, UseRegex, CurrentScope, scopeFilter);
            SearchResults = new ObservableCollection<SearchResultItem>(results);
            SearchStatus = $"{results.Count} match{(results.Count == 1 ? string.Empty : "es")}";
        }
        catch (ArgumentException ex)
        {
            SearchResults = [];
            SearchStatus = "Invalid pattern: " + ex.Message;
        }
        AddToHistory(SearchQuery);
    }

    [RelayCommand]
    private void PreviewReplace()
    {
        if (SearchResults.Count == 0) return;
        _searchAndReplace.PreviewReplace(SearchResults.ToList(), ReplaceText, UseRegex);
        SearchResults = new ObservableCollection<SearchResultItem>(SearchResults);
    }

    [RelayCommand]
    private async Task ExecuteReplace()
    {
        if (SearchResults.Count == 0 || string.IsNullOrEmpty(SearchQuery) || IsAuditMode) return;
        if (SearchResults.Count > 20 && !await _messageService.ConfirmAsync($"Replace in {SearchResults.Count} values?", "Replace All", "Replace", "Cancel"))
            return;

        var before = new Dictionary<TranslationItem, string>(ReferenceEqualityComparer.Instance);
        foreach (var t in AllTranslation) before[t] = t.Value;
        var count = _searchAndReplace.ApplyReplace(AllTranslation, SearchResults.ToList(), SearchQuery, ReplaceText, UseRegex);

        var changed = AllTranslation.Where(t => before.TryGetValue(t, out var old) && old != t.Value).ToList();
        foreach (var t in changed) _undoRedoService.Record(t.Namespace, t.Language, before[t], t.Value);
        NotifyBulkValueChanges(changed);
        foreach (var vm in PagingController.Data.SelectMany(g => g.AllItems)) vm.Refresh();
        UpdateSummaryInfo();

        StatusText = $"Replaced {count} occurrence(s)";
        ExecuteSearch();
    }

    [RelayCommand]
    private void ToggleRegex()
    {
        UseRegex = !UseRegex;
        if (!string.IsNullOrEmpty(SearchQuery)) ExecuteSearch();
    }

    [RelayCommand]
    private void ToggleReplacePanel() => ShowReplacePanel = !ShowReplacePanel;

    [RelayCommand]
    private void ClearSearchHistory() => SearchHistory.Clear();

    [RelayCommand]
    private void UseHistoryEntry(string? query)
    {
        if (string.IsNullOrEmpty(query)) return;
        SearchQuery = query;
        ExecuteSearch();
    }

    private void AddToHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        SearchHistory.Remove(query);
        SearchHistory.Insert(0, query);
        while (SearchHistory.Count > MaxHistorySize) SearchHistory.RemoveAt(SearchHistory.Count - 1);
    }
}
