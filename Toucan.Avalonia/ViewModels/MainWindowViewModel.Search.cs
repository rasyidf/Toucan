using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Locales;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Find &amp; replace across translation keys and values (Search panel).</summary>
public partial class MainWindowViewModel
{
    private const int MaxHistorySize = 20;

    /// <summary>Query for the Search panel, separate from the editor filter so typing doesn't re-filter the editor.</summary>
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private string replaceText = string.Empty;
    [ObservableProperty] private bool matchCase;
    [ObservableProperty] private bool wholeWord;
    [ObservableProperty] private bool useRegex;
    [ObservableProperty] private bool preserveCase;
    [ObservableProperty] private bool showReplacePanel;
    /// <summary>The "..." section with the language and key filters.</summary>
    [ObservableProperty] private bool showSearchDetails;
    [ObservableProperty] private string includeLanguages = string.Empty;
    [ObservableProperty] private string includeKeys = string.Empty;
    [ObservableProperty] private string excludeKeys = string.Empty;
    [ObservableProperty] private ObservableCollection<SearchGroupViewModel> searchGroups = [];
    [ObservableProperty] private ObservableCollection<string> searchHistory = [];
    [ObservableProperty] private SearchMatchViewModel? selectedSearchMatch;
    [ObservableProperty] private string searchStatus = string.Empty;

    /// <summary>Every match, flat; replace works on this. The panel shows <see cref="SearchGroups"/>.</summary>
    private List<SearchResultItem> _searchResults = [];
    private readonly HashSet<string> _collapsedSearchKeys = new(StringComparer.Ordinal);

    public bool HasSearchResults => SearchGroups.Count > 0;
    public int SearchMatchCount => _searchResults.Count;
    public bool HasFilters => !string.IsNullOrWhiteSpace(IncludeLanguages) || !string.IsNullOrWhiteSpace(IncludeKeys) || !string.IsNullOrWhiteSpace(ExcludeKeys);

    private SearchOptions CurrentSearchOptions => new(
        MatchCase, WholeWord, UseRegex, PreserveCase,
        SearchOptions.ParseList(IncludeLanguages), SearchOptions.ParseList(IncludeKeys), SearchOptions.ParseList(ExcludeKeys));

    // Typing or flipping an option searches after a short pause, like an editor's find-in-files.
    partial void OnSearchQueryChanged(string value) => ScheduleSearch();
    partial void OnMatchCaseChanged(bool value) => ScheduleSearch();
    partial void OnWholeWordChanged(bool value) => ScheduleSearch();
    partial void OnUseRegexChanged(bool value) => ScheduleSearch();
    partial void OnIncludeLanguagesChanged(string value) { OnPropertyChanged(nameof(HasFilters)); ScheduleSearch(); }
    partial void OnIncludeKeysChanged(string value) { OnPropertyChanged(nameof(HasFilters)); ScheduleSearch(); }
    partial void OnExcludeKeysChanged(string value) { OnPropertyChanged(nameof(HasFilters)); ScheduleSearch(); }
    partial void OnReplaceTextChanged(string value) => RefreshReplacePreview();
    partial void OnPreserveCaseChanged(bool value) => RefreshReplacePreview();
    partial void OnShowReplacePanelChanged(bool value) => RefreshReplacePreview();

    partial void OnSelectedSearchMatchChanged(SearchMatchViewModel? oldValue, SearchMatchViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue is null) return;
        newValue.IsSelected = true;
        RevealKey(newValue.Key);
    }

    [RelayCommand]
    private void OpenSearchPanel() => SidePanelRegistry.Instance.Activate("search");

    private void ScheduleSearch()
    {
        _panelSearchTimer.Stop();
        if (string.IsNullOrEmpty(SearchQuery))
        {
            ClearSearchResults();
            return;
        }
        _panelSearchTimer.Start();
    }

    /// <summary>Search now (Enter, Refresh) and remember the query in the history.</summary>
    [RelayCommand]
    private void ExecuteSearch()
    {
        _panelSearchTimer.Stop();
        RunSearch();
        if (!string.IsNullOrWhiteSpace(SearchQuery)) AddToHistory(SearchQuery);
    }

    private void RunSearch()
    {
        if (string.IsNullOrEmpty(SearchQuery))
        {
            ClearSearchResults();
            return;
        }

        FlushPendingEdits();
        try
        {
            _searchResults = [.. _searchAndReplace.Search(AllTranslation, SearchQuery, CurrentSearchOptions)];
            RefreshReplacePreview(rebuild: false);
            RebuildSearchGroups();
            var keys = SearchGroups.Count;
            SearchStatus = _searchResults.Count switch
            {
                0 => Loc.T("No results"),
                1 => Loc.T("1 result in 1 key"),
                _ when keys == 1 => Loc.T("{0} results in 1 key").Replace("{0}", _searchResults.Count.ToString(), StringComparison.Ordinal),
                _ => Loc.T("{0} results in {1} keys").Replace("{0}", _searchResults.Count.ToString(), StringComparison.Ordinal).Replace("{1}", keys.ToString(), StringComparison.Ordinal),
            };
        }
        catch (ArgumentException ex)
        {
            ClearSearchResults();
            SearchStatus = Loc.T("Invalid pattern: ") + ex.Message;
        }
    }

    private void RebuildSearchGroups()
    {
        SearchGroups = new ObservableCollection<SearchGroupViewModel>(
            _searchResults
                .GroupBy(r => r.Key)
                .Select(g => new SearchGroupViewModel(g.Key, g.Select(r => new SearchMatchViewModel(r)), !_collapsedSearchKeys.Contains(g.Key), OnSearchGroupToggled)));
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(SearchMatchCount));
    }

    private void OnSearchGroupToggled(SearchGroupViewModel group)
    {
        if (group.IsExpanded) _collapsedSearchKeys.Remove(group.Key);
        else _collapsedSearchKeys.Add(group.Key);
    }

    /// <summary>Forget the results (query emptied, project closed).</summary>
    private void ClearSearchResults()
    {
        _searchResults = [];
        _collapsedSearchKeys.Clear();
        SearchGroups = [];
        SelectedSearchMatch = null;
        SearchStatus = string.Empty;
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(SearchMatchCount));
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _panelSearchTimer.Stop();
        SearchQuery = string.Empty;
        ReplaceText = string.Empty;
        ClearSearchResults();
    }

    [RelayCommand]
    private void CollapseAllSearchGroups()
    {
        foreach (var g in SearchGroups) g.IsExpanded = false;
    }

    [RelayCommand]
    private void ExpandAllSearchGroups()
    {
        foreach (var g in SearchGroups) g.IsExpanded = true;
    }

    [RelayCommand]
    private void SelectSearchMatch(SearchMatchViewModel? match) => SelectedSearchMatch = match;

    /// <summary>Shows each match's replacement inline while the replace box is open.</summary>
    private void RefreshReplacePreview(bool rebuild = true)
    {
        if (_searchResults.Count == 0) return;
        if (ShowReplacePanel && !string.IsNullOrEmpty(SearchQuery))
        {
            try { _searchAndReplace.PreviewReplace(_searchResults, SearchQuery, ReplaceText, CurrentSearchOptions); }
            catch (ArgumentException) { return; }
        }
        else
        {
            foreach (var r in _searchResults) { r.ReplacedValue = null; r.ReplacementText = null; }
        }
        if (rebuild) foreach (var m in SearchGroups.SelectMany(g => g.Matches)) m.RefreshReplacement();
    }

    [RelayCommand]
    private async Task ExecuteReplace()
    {
        if (string.IsNullOrEmpty(SearchQuery) || IsAuditMode) return;
        var targets = _searchResults.Where(r => !r.InKey).ToList(); // matches inside a key are shown but never rewritten
        if (targets.Count == 0) return;
        if (targets.Count > 20 && !await _messageService.ConfirmAsync($"Replace in {targets.Count} values?", "Replace All", "Replace", "Cancel"))
            return;

        var before = new Dictionary<TranslationItem, string>(ReferenceEqualityComparer.Instance);
        foreach (var t in AllTranslation) before[t] = t.Value;
        var count = _searchAndReplace.ApplyReplace(AllTranslation, targets, SearchQuery, ReplaceText, CurrentSearchOptions);

        var changed = AllTranslation.Where(t => before.TryGetValue(t, out var old) && old != t.Value).ToList();
        foreach (var t in changed) _undoRedoService.Record(t.Namespace, t.Language, before[t], t.Value);
        NotifyBulkValueChanges(changed);
        foreach (var vm in PagingController.Data.SelectMany(g => g.AllItems)) vm.Refresh();
        UpdateSummaryInfo();

        StatusText = $"Replaced {count} occurrence(s)";
        ExecuteSearch();
    }

    [RelayCommand]
    private void ToggleReplacePanel() => ShowReplacePanel = !ShowReplacePanel;

    [RelayCommand]
    private void ToggleSearchDetails() => ShowSearchDetails = !ShowSearchDetails;

    /// <summary>Limit the search to the key path selected in the tree.</summary>
    [RelayCommand]
    private void UseSelectedKeyPath()
    {
        var path = SelectedNode?.Namespace ?? SelectedGroup?.Namespace;
        if (!string.IsNullOrEmpty(path)) IncludeKeys = path + "*";
    }

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

/// <summary>All matches inside one key; collapses like a file in an editor's search results.</summary>
public sealed partial class SearchGroupViewModel : ObservableObject
{
    private readonly Action<SearchGroupViewModel> _toggled;

    public SearchGroupViewModel(string key, IEnumerable<SearchMatchViewModel> matches, bool expanded, Action<SearchGroupViewModel> toggled)
    {
        Key = key;
        Matches = [.. matches];
        isExpanded = expanded;
        _toggled = toggled;
    }

    public string Key { get; }
    public IReadOnlyList<SearchMatchViewModel> Matches { get; }
    public int Count => Matches.Count;

    [ObservableProperty] private bool isExpanded;

    partial void OnIsExpandedChanged(bool value) => _toggled(this);

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>One match row: the text around it, where the match is, and what replacing it would produce.</summary>
public sealed partial class SearchMatchViewModel(SearchResultItem result) : ObservableObject
{
    public SearchResultItem Result { get; } = result;

    [ObservableProperty] private bool isSelected;
    public string Key => Result.Key;
    public string Language => Result.Language;
    public string Text => Result.Value;
    public int MatchStart => Result.MatchStart;
    public int MatchLength => Result.MatchLength;
    public bool InKey => Result.InKey;

    /// <summary>The replacement for this match while the replace box is open; null otherwise.</summary>
    public string? Replacement => Result.ReplacementText;

    public void RefreshReplacement() => OnPropertyChanged(nameof(Replacement));
}
