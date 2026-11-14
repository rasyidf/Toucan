using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Filtering, paging, tree selection, editor modes, zen/focused editing, and language visibility.</summary>
public partial class MainWindowViewModel
{
    // ───────────────────────── Search & filter ─────────────────────────

    /// <summary>Human-readable description of the active filter, shown above the editor.</summary>
    [ObservableProperty] private string activeFilterDescription = string.Empty;

    /// <summary>Active language+status filter, e.g. "empty:fr". Null when inactive.</summary>
    [ObservableProperty] private string? activeLanguageFilter;

    /// <summary>Source usage filter: null (off), "used", or "unused".</summary>
    [ObservableProperty] private string? filteredBySourceUsage;

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    /// <summary>
    /// Filters the editor. A query that ends with "." or exactly names a key filters by key path;
    /// anything else is a fuzzy search over keys and values.
    /// </summary>
    internal void Search(string? query, bool alwaysPaging = false)
    {
        if (HasDocumentNavigation) FlushPendingEdits();
        _searchDebounce.Stop();
        query = query?.Trim() ?? string.Empty;
        ActiveLanguageFilter = null;

        List<string> matched;
        var parsable = AllTranslation.ForParse().ToList();
        if (string.IsNullOrEmpty(query))
        {
            matched = parsable.Select(t => t.Namespace).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        else if (query.EndsWith('.'))
        {
            matched = parsable.Select(t => t.Namespace).Distinct()
                .Where(n => n.StartsWith(query, StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
        }
        else if (parsable.Any(t => t.Namespace == query))
        {
            matched = [query];
        }
        else
        {
            matched = _fuzzySearch.Search(parsable, query).Select(m => m.Item.Namespace).Distinct().ToList();
        }

        if (FilteredBySourceUsage != null && _sourceCodeService.HasScanData)
        {
            var unused = _sourceCodeService.GetUnusedKeys(matched).ToHashSet(StringComparer.Ordinal);
            matched = FilteredBySourceUsage == "unused"
                ? matched.Where(unused.Contains).ToList()
                : matched.Where(_sourceCodeService.GetAllKeys().ToHashSet(StringComparer.Ordinal).Contains).ToList();
        }

        var truncated = !alwaysPaging && matched.Count > AppOptions.TruncateResultsOver;
        if (truncated) matched = matched.Take(AppOptions.TruncateResultsOver).ToList();

        SetEditorGroups(BuildLanguageGroups(matched, parsable), truncated);

        ActiveFilterDescription = string.IsNullOrEmpty(query)
            ? (FilteredBySourceUsage != null ? $"Keys {FilteredBySourceUsage} in source code" : string.Empty)
            : $"Filter: “{query}”";
        StatusText = $"{matched.Count} key(s){(truncated ? " (truncated)" : string.Empty)}";
    }

    /// <summary>Builds editor cards, merging plural variants (key_one, key_other, ...) into one card.</summary>
    private List<LanguageGroupViewModel> BuildLanguageGroups(List<string> namespaces, IEnumerable<TranslationItem> allItems)
    {
        if (HasDocumentNavigation)
        {
            var matched = namespaces.ToHashSet(StringComparer.Ordinal);
            var identified = Workspace.SnapshotEntriesAsync().GetAwaiter().GetResult()!.Units.GroupBy(u => u.EntryId).ToDictionary(g => g.Key, g => g.First());
            return allItems.Where(t => matched.Contains(t.Namespace) && !IsNamespaceHidden(t.Namespace))
                .GroupBy(t => Workspace.GetProjectionEntryId(t) ?? throw new InvalidOperationException("Document editor row has no active entry identity."))
                .Where(g => _documentSelection is null || _documentSelection.Contains(g.Key))
                .OrderBy(g => g.First().Namespace, StringComparer.Ordinal).ThenBy(g => g.Key)
                .Select(g => { var unit = identified[g.Key]; var card = CreateGroup(g.First().Namespace, g.Key, unit.Namespace, unit.KeyPath); card.LoadTranslations(g); return card; }).ToList();
        }
        var byNamespace = allItems.GroupBy(t => t.Namespace).ToDictionary(g => g.Key, g => g.ToList());
        var groups = new List<LanguageGroupViewModel>();
        var consumed = new HashSet<string>();

        foreach (var n in namespaces)
        {
            if (!consumed.Add(n) || IsNamespaceHidden(n)) continue;

            if (PluralService.IsPluralKey(n))
            {
                var baseKey = PluralService.GetBaseKey(n);
                var siblings = namespaces
                    .Where(ns => ns != n && !consumed.Contains(ns) && PluralService.IsPluralKey(ns) && PluralService.GetBaseKey(ns) == baseKey)
                    .Prepend(n)
                    .ToList();

                if (siblings.Count > 1)
                {
                    foreach (var s in siblings) consumed.Add(s);
                    var plural = CreateGroup(baseKey);
                    plural.LoadPluralVariants(siblings
                        .Where(byNamespace.ContainsKey)
                        .SelectMany(s => byNamespace[s])
                        .GroupBy(t => t.Namespace));
                    groups.Add(plural);
                    continue;
                }
            }

            var group = CreateGroup(n);
            group.LoadTranslations(byNamespace.GetValueOrDefault(n) ?? []);
            groups.Add(group);
        }
        return groups;
    }

    private void FilterAndDisplay(List<TranslationItem> matched, string description, string emptyMessage)
    {
        var namespaces = matched.ToNamespaces().OrderBy(n => n, StringComparer.Ordinal).ToList();
        SetEditorGroups(BuildLanguageGroups(namespaces, AllTranslation.ForParse()));
        ActiveFilterDescription = description;
        StatusText = namespaces.Count == 0 ? emptyMessage : $"{namespaces.Count} key(s) · {description}";
    }

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ApplySearch()
    {
        Search(SearchText);
        RecordFilterHistory(SearchText);
    }

    [RelayCommand]
    private void ShowUntranslated() =>
        FilterAndDisplay(AllTranslation.Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList(), "Untranslated", "No untranslated items found.");

    [RelayCommand]
    private void ShowTranslated() =>
        FilterAndDisplay(AllTranslation.Where(t => !string.IsNullOrWhiteSpace(t.Value)).ToList(), "Translated", "No translated items found.");

    [RelayCommand]
    private void ShowApproved() =>
        FilterAndDisplay(AllTranslation.Where(t => t.IsApproved).ToList(), "Approved", "No approved items found.");

    [RelayCommand]
    private void ShowNeedsReview() =>
        FilterAndDisplay(AllTranslation.Where(t => !string.IsNullOrWhiteSpace(t.Value) && !t.IsApproved).ToList(), "Needs review", "Everything is approved.");

    [RelayCommand]
    private void ShowChangedThisSession() =>
        FilterAndDisplay(AllTranslation.Where(t => SessionDirtyKeys.Contains(t.Namespace)).ToList(), "Changed this session", "No changes yet.");

    [RelayCommand]
    private void ShowMachineTranslated() =>
        FilterAndDisplay(AllTranslation.Where(t => t.ChangeType == Toucan.Core.Contracts.Services.ChangeType.Suggestion).ToList(), "Machine translated", "No machine-translated items found.");

    [RelayCommand]
    private void ClearFilter()
    {
        ClearDocumentSelection();
        FilteredBySourceUsage = null;
        SelectedNode = null;
        if (SearchText.Length == 0) Search(string.Empty, true);
        else SearchText = string.Empty;
    }

    [RelayCommand]
    private void ShowAll() { ClearDocumentSelection(); Search(SearchText, true); }

    /// <summary>Filters by language and status. Parameter format: "status:language" (translated|empty|needsreview|approved).</summary>
    [RelayCommand]
    private void FilterByLanguageStatus(string? parameter)
    {
        var colon = parameter?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (parameter == null || colon <= 0 || colon >= parameter.Length - 1) return;

        var status = parameter[..colon].ToLowerInvariant();
        var language = parameter[(colon + 1)..];
        var items = AllTranslation.Where(t => t.Language == language && !string.IsNullOrWhiteSpace(t.Namespace)).ToList();
        var matched = status switch
        {
            "translated" => items.Where(t => !string.IsNullOrEmpty(t.Value)).ToList(),
            "empty" => items.Where(t => string.IsNullOrEmpty(t.Value)).ToList(),
            "needsreview" => items.Where(t => t.Language != PrimaryLanguage && t.Permissions.EditMetadata && !string.IsNullOrEmpty(t.Value) && !t.IsApproved).ToList(),
            "approved" => items.Where(t => t.IsApproved).ToList(),
            _ => items
        };

        FilterAndDisplay(matched, $"{language} · {status}", $"No {status} items for {language}.");
        ActiveLanguageFilter = parameter;
    }

    [RelayCommand] private void FilterLanguageTranslated(SummaryItem? item) => FilterByLanguageStatus(item == null ? null : "translated:" + item.Language);
    [RelayCommand] private void FilterLanguageEmpty(SummaryItem? item) => FilterByLanguageStatus(item == null ? null : "empty:" + item.Language);
    [RelayCommand] private void FilterLanguageNeedsReview(SummaryItem? item) => FilterByLanguageStatus(item == null ? null : "needsreview:" + item.Language);
    [RelayCommand] private void FilterLanguageApproved(SummaryItem? item) => FilterByLanguageStatus(item == null ? null : "approved:" + item.Language);

    // ───────────────────────── Tree selection ─────────────────────────

    partial void OnSelectedNodeChanged(NsTreeItem? value)
    {
        RenameItemCommand.NotifyCanExecuteChanged();
        DeleteItemCommand.NotifyCanExecuteChanged();
        DuplicateItemCommand.NotifyCanExecuteChanged();

        if (value == null)
        {
            SelectedNodePath = [];
            StatusBarService.Instance.UpdateCursor(string.Empty);
            return;
        }

        SelectedNodePath = new ObservableCollection<string>(value.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries));
        value.IsExpanded = true;
        StatusBarService.Instance.UpdateCursor(value.Namespace);

        var filter = value.HasItems ? value.Namespace + "." : value.Namespace;
        if (SearchText != filter) SearchText = filter;
        RefreshInspector();
    }

    partial void OnSelectedGroupChanged(LanguageGroupViewModel? value) => RefreshInspector();

    /// <summary>Selects a key from outside the tree (search results, issues, source usages).</summary>
    [RelayCommand]
    private void RevealKey(string? ns)
    {
        if (string.IsNullOrEmpty(ns)) return;
        ClearDocumentSelection();
        SearchText = ns;
        Search(ns, true);
        SelectedGroup = PagingController.Data.FirstOrDefault(g => g.Namespace == ns || g.PluralVariants.Any(v => v.FullNamespace == ns));
    }

    // ───────────────────────── Pagination ─────────────────────────

    [RelayCommand]
    private void NextPage()
    {
        FlushPendingEdits();
        PagingController.NextPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        FlushPendingEdits();
        PagingController.PreviousPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void FirstPage()
    {
        FlushPendingEdits();
        PagingController.MoveFirst();
        PagedUpdates();
    }

    [RelayCommand]
    private void LastPage()
    {
        FlushPendingEdits();
        PagingController.LastPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void GoToPage(PaginationButton? button)
    {
        if (button == null || button.IsEllipsis) return;
        FlushPendingEdits();
        PagingController.GoTo(button.Number);
        PagedUpdates();
    }

    // ───────────────────────── View mode ─────────────────────────

    [RelayCommand]
    private void ToggleViewMode()
    {
        if (HasDocumentNavigation) NavigationLayout = NavigationLayout == WorkspaceNavigationLayout.NamespaceFirst
            ? WorkspaceNavigationLayout.FileFirst : WorkspaceNavigationLayout.NamespaceFirst;
        else IsTreeView = !IsTreeView;
    }

    [RelayCommand]
    private void ToggleFullscreen() => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    [ObservableProperty] private bool infiniteScroll;

    [RelayCommand]
    private async Task ToggleInfiniteScroll()
    {
        var total = PagingController.Data.Count;
        if (!InfiniteScroll && total > 500
            && !await _messageService.ConfirmAsync($"You have {total} items. Showing all of them at once may make scrolling slow. Continue?", "Show All Items"))
        {
            return;
        }

        FlushPendingEdits();
        InfiniteScroll = !InfiniteScroll;
        PagingController.UpdatePageSize(InfiniteScroll ? int.MaxValue : EffectivePageSize);
        PagedUpdates();
    }

    // ───────────────────────── Filter history ─────────────────────────

    [ObservableProperty] private ObservableCollection<string> filterHistory = [];

    private void RecordFilterHistory(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        FilterHistory.Remove(filter);
        FilterHistory.Insert(0, filter);
        while (FilterHistory.Count > 15) FilterHistory.RemoveAt(FilterHistory.Count - 1);
        AppOptions.FilterHistory = [.. FilterHistory];
        _preferenceService.Save(AppOptions);
    }

    internal void LoadFilterHistory()
    {
        FilterHistory = new ObservableCollection<string>(AppOptions.FilterHistory ?? []);
    }

    // ───────────────────────── Editor modes ─────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorMode), nameof(IsReviewMode), nameof(IsAuditMode))]
    private EditorMode editorMode;

    public bool IsEditorMode => EditorMode == EditorMode.Editor;
    public bool IsReviewMode => EditorMode == EditorMode.Review;
    public bool IsAuditMode => EditorMode == EditorMode.Audit;

    private string? _preReviewFilter;

    partial void OnEditorModeChanged(EditorMode oldValue, EditorMode newValue)
    {
        PanelService.Instance.EditorMode = newValue;
        StatusBarService.Instance.UpdateEditorMode(newValue.ToString());
        RefreshFocusedActions();

        if (newValue == EditorMode.Review)
        {
            _preReviewFilter = SearchText;
            ApplyReviewFilter();
        }
        else if (oldValue == EditorMode.Review)
        {
            SearchText = _preReviewFilter ?? string.Empty;
            _preReviewFilter = null;
            Search(SearchText, true);
        }
    }

    /// <summary>Review mode shows only actionable items: untranslated or not yet approved.</summary>
    private void ApplyReviewFilter()
    {
        if (AllTranslation.Count == 0) return;
        FilterAndDisplay(
            AllTranslation.Where(t => string.IsNullOrWhiteSpace(t.Value) || !t.IsApproved).ToList(),
            "Review: unapproved or untranslated",
            "All items are approved and translated.");
    }

    [RelayCommand] private void SwitchToEditorMode() => EditorMode = EditorMode.Editor;
    [RelayCommand] private void SwitchToReviewMode() => EditorMode = EditorMode.Review;
    [RelayCommand] private void SwitchToAuditMode() => EditorMode = EditorMode.Audit;

    [RelayCommand]
    private void CycleEditorMode() => EditorMode = EditorMode switch
    {
        EditorMode.Editor => EditorMode.Review,
        EditorMode.Review => EditorMode.Audit,
        _ => EditorMode.Editor
    };

    // ───────────────────────── Zen / focused editor ─────────────────────────

    private EditorLayout _lastListLayout = EditorLayout.Cards;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedEditorMode), nameof(IsCardLayout), nameof(IsRowLayout), nameof(EditorLayoutLabel))]
    private EditorLayout editorLayout;

    public bool FocusedEditorMode => EditorLayout == EditorLayout.Focused;
    public bool IsCardLayout => EditorLayout == EditorLayout.Cards;
    public bool IsRowLayout => EditorLayout == EditorLayout.Rows;
    public string EditorLayoutLabel => Locales.Loc.T(EditorLayout switch
    {
        EditorLayout.Rows => "Line by line",
        EditorLayout.Focused => "Focused",
        _ => "Cards"
    });

    [RelayCommand]
    private void SetEditorLayout(EditorLayout layout) => EditorLayout = layout;

    partial void OnEditorLayoutChanging(EditorLayout value) => FlushPendingEdits();

    partial void OnEditorLayoutChanged(EditorLayout oldValue, EditorLayout newValue)
    {
        if (newValue == EditorLayout.Focused)
        {
            _lastListLayout = oldValue;
            var selectedIndex = SelectedGroup == null ? -1 : PagingController.Data.IndexOf(SelectedGroup);
            FocusedIndex = Math.Max(0, selectedIndex);
            SelectedGroup = ZenCurrentItem;
        }
        else if (oldValue == EditorLayout.Focused)
        {
            SelectedGroup = ZenCurrentItem;
            PagingController.GoTo(FocusedIndex / Math.Max(1, PagingController.PageSize) + 1);
            PagedUpdates();
        }
        AppOptions.EditorLayout = newValue.ToString();
        _preferenceService.Save(AppOptions);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZenCurrentItem), nameof(FocusedPositionText))]
    [NotifyCanExecuteChangedFor(nameof(FocusedNextCommand), nameof(FocusedPreviousCommand))]
    private int focusedIndex;

    [ObservableProperty] private bool zenMode;

    /// <summary>The card shown in Zen / focused mode.</summary>
    public LanguageGroupViewModel? ZenCurrentItem
    {
        get
        {
            var data = PagingController.Data;
            return data.Count == 0 ? null : data[Math.Clamp(FocusedIndex, 0, data.Count - 1)];
        }
    }

    public string FocusedPositionText => PagingController.Data.Count == 0
        ? "0 / 0"
        : $"{Math.Clamp(FocusedIndex, 0, PagingController.Data.Count - 1) + 1} / {PagingController.Data.Count}";

    partial void OnZenModeChanged(bool value)
    {
        if (value != PanelService.Instance.ZenMode) PanelService.Instance.ToggleZenMode();
        if (value)
        {
            var max = PagingController.Data.Count;
            if (FocusedIndex >= max && max > 0) FocusedIndex = max - 1;
            OnPropertyChanged(nameof(ZenCurrentItem));
        }
    }

    [RelayCommand]
    private void ToggleZenMode()
    {
        FlushPendingEdits();
        ZenMode = !ZenMode;
    }

    [RelayCommand]
    private void ToggleFocusedEditor()
    {
        EditorLayout = FocusedEditorMode ? _lastListLayout : EditorLayout.Focused;
    }

    private bool CanFocusedNext() => FocusedIndex < PagingController.Data.Count - 1;
    private bool CanFocusedPrevious() => FocusedIndex > 0 && PagingController.Data.Count > 0;

    partial void OnFocusedIndexChanged(int value)
    {
        if (FocusedEditorMode || ZenMode) SelectedGroup = ZenCurrentItem;
    }

    [RelayCommand(CanExecute = nameof(CanFocusedNext))]
    private void FocusedNext()
    {
        if (FocusedIndex < PagingController.Data.Count - 1)
        {
            ZenCurrentItem?.FlushEdits();
            FocusedIndex++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFocusedPrevious))]
    private void FocusedPrevious()
    {
        if (FocusedIndex > 0)
        {
            ZenCurrentItem?.FlushEdits();
            FocusedIndex--;
        }
    }

    // ───────────────────────── Review loop ─────────────────────────

    /// <summary>A card still needs review when any of its non-empty target values is not approved, or any value is empty.</summary>
    private static bool NeedsReview(LanguageGroupViewModel card) =>
        card.AllItems.Any(i => i.IsEmpty || (!i.Model.IsApproved && !string.IsNullOrEmpty(i.Model.Value)));

    private bool CanAdvanceReview() => PagingController.Data.Count > 0;

    /// <summary>
    /// Moves focus to the next card after the current one that still needs review (an empty or unapproved value),
    /// deterministically by index without wrapping. Falls back to the plain next card when none needs review.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdvanceReview))]
    private void FocusedNextNeedingReview()
    {
        var data = PagingController.Data;
        if (data.Count == 0) return;
        ZenCurrentItem?.FlushEdits();
        for (var i = FocusedIndex + 1; i < data.Count; i++)
        {
            if (NeedsReview(data[i])) { FocusedIndex = i; return; }
        }
        // Nothing left to review ahead: advance one if we can, otherwise stay put.
        if (FocusedIndex < data.Count - 1) FocusedIndex++;
        else StatusText = Locales.Loc.T("No more items need review.");
    }

    /// <summary>
    /// Approves the current card's eligible target values (non-empty, not yet approved), then advances to the next card
    /// that needs review. The keyboard review loop: approve → next, without leaving the keyboard or losing the place.
    /// </summary>
    private void ApproveCurrentCardAndNext()
    {
        var card = ZenCurrentItem;
        if (card is null || IsAuditMode) return;
        card.FlushEdits();
        var eligible = card.AllItems
            .Select(i => i.Model)
            .Where(t => t.Language != PrimaryLanguage && t.Permissions.EditMetadata && !string.IsNullOrEmpty(t.Value) && !t.IsApproved)
            .ToList();
        if (eligible.Count > 0) ApproveItems(eligible);
        FocusedNextNeedingReview();
    }

    // ───────────────────────── Language visibility ─────────────────────────

    public ObservableCollection<LanguageVisibilityItem> LanguageVisibilityFilter { get; } = [];

    internal void InitLanguageVisibilityFilter(IEnumerable<string> languages)
    {
        var previous = LanguageVisibilityFilter.ToDictionary(i => i.Language, i => i.IsVisible);
        foreach (var item in LanguageVisibilityFilter) item.PropertyChanged -= OnLanguageVisibilityChanged;
        LanguageVisibilityFilter.Clear();
        foreach (var lang in languages)
        {
            var item = new LanguageVisibilityItem { Language = lang, IsVisible = previous.GetValueOrDefault(lang, true) };
            item.PropertyChanged += OnLanguageVisibilityChanged;
            LanguageVisibilityFilter.Add(item);
        }
    }

    private void OnLanguageVisibilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LanguageVisibilityItem.IsVisible)) ApplyLanguageVisibility();
    }

    [RelayCommand]
    private void ShowAllLanguages()
    {
        foreach (var item in LanguageVisibilityFilter) item.IsVisible = true;
    }

    internal void ApplyLanguageVisibility()
    {
        if (LanguageVisibilityFilter.Count == 0) return;
        var visible = LanguageVisibilityFilter.Where(f => f.IsVisible).Select(f => f.Language).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in PagingController.PageData.Append(ZenCurrentItem))
        {
            if (group == null) continue;
            foreach (var ti in group.AllItems) ti.IsLanguageVisible = visible.Contains(ti.Language);
        }
    }

    // ───────────────────────── Suggestions ─────────────────────────

    [ObservableProperty] private TranslationItemViewModel? focusedTranslationItem;

    partial void OnFocusedTranslationItemChanged(TranslationItemViewModel? oldValue, TranslationItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsFocused = false;
            oldValue.PropertyChanged -= OnFocusedItemPropertyChanged;
        }
        RefreshFocusedActions();
        if (newValue == null) return;
        newValue.IsFocused = true;
        var group = PagingController.PageData.FirstOrDefault(g => g.AllItems.Contains(newValue));
        if (group != null && !ReferenceEquals(group, SelectedGroup)) SelectedGroup = group;
        UpdateGhostSuggestion(newValue);
        newValue.PropertyChanged -= OnFocusedItemPropertyChanged;
        newValue.PropertyChanged += OnFocusedItemPropertyChanged;
    }

    [RelayCommand]
    private void InsertSuggestion(string? suggestion)
    {
        if (string.IsNullOrEmpty(suggestion)) return;
        if (FocusedTranslationItem == null)
        {
            StatusText = "Click into a translation field first.";
            return;
        }
        if (IsAuditMode) return;
        FocusedTranslationItem.Value = suggestion;
    }
}
