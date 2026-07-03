using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

/// <summary>
/// Search &amp; Replace state and commands (FG-07).
/// </summary>
internal partial class MainWindowViewModel
{
    /// <summary>Set by DI wiring after construction (same pattern as FuzzySearchService).</summary>
    internal ISearchAndReplaceService? SearchAndReplaceService { get; set; }

    /// <summary>
    /// Dedicated search query for the Search &amp; Replace panel.
    /// Separated from <see cref="SearchText"/> to avoid triggering debounced navigation filter on every keystroke.
    /// </summary>
    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string replaceText = string.Empty;

    [ObservableProperty]
    private bool useRegex;

    [ObservableProperty]
    private SearchScope searchScope = SearchScope.AllLanguages;

    [ObservableProperty]
    private ObservableCollection<SearchResultItem> searchResults = [];

    [ObservableProperty]
    private ObservableCollection<string> searchHistory = [];

    [ObservableProperty]
    private bool showReplacePanel;

    [ObservableProperty]
    private SearchResultItem? selectedSearchResult;

    private const int MaxHistorySize = 20;

    [RelayCommand]
    private void ExecuteSearch()
    {
        if (SearchAndReplaceService is null || string.IsNullOrEmpty(SearchQuery))
        {
            SearchResults.Clear();
            return;
        }

        // Determine scope filter value
        string? scopeFilter = SearchScope switch
        {
            SearchScope.SpecificLanguage => SelectedNode?.Name, // ponytail: reuse selected node as language hint
            SearchScope.CurrentNamespace => SelectedNode?.Namespace,
            _ => null
        };

        var results = SearchAndReplaceService.Search(
            AllTranslation, SearchQuery, UseRegex, SearchScope, scopeFilter);

        SearchResults = new ObservableCollection<SearchResultItem>(results);
        StatusText = $"{results.Count} match{(results.Count == 1 ? "" : "es")} found";

        AddToHistory(SearchQuery);
    }

    [RelayCommand]
    private void PreviewReplace()
    {
        if (SearchAndReplaceService is null || SearchResults.Count == 0)
            return;

        SearchAndReplaceService.PreviewReplace(
            SearchResults.ToList(), ReplaceText, UseRegex);

        // Trigger UI refresh
        OnPropertyChanged(nameof(SearchResults));
    }

    [RelayCommand]
    private void ExecuteReplace()
    {
        if (SearchAndReplaceService is null || SearchResults.Count == 0 || string.IsNullOrEmpty(SearchQuery))
            return;

        int count = SearchAndReplaceService.ApplyReplace(
            AllTranslation, SearchResults.ToList(), SearchQuery, ReplaceText, UseRegex);

        StatusText = $"Replaced {count} item{(count == 1 ? "" : "s")}";
        IsDirty = true;

        // Re-run search to show updated state
        ExecuteSearch();
    }

    [RelayCommand]
    private void ToggleRegex()
    {
        UseRegex = !UseRegex;
        // Re-run search if there's an active query
        if (!string.IsNullOrEmpty(SearchQuery))
            ExecuteSearch();
    }

    [RelayCommand]
    private void ToggleReplacePanel()
    {
        ShowReplacePanel = !ShowReplacePanel;
    }

    [RelayCommand]
    private void ClearSearchHistory()
    {
        SearchHistory.Clear();
    }

    private void AddToHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        // Remove duplicate if already in history
        SearchHistory.Remove(query);

        // Insert at front
        SearchHistory.Insert(0, query);

        // Trim to max size
        while (SearchHistory.Count > MaxHistorySize)
            SearchHistory.RemoveAt(SearchHistory.Count - 1);
    }
}
