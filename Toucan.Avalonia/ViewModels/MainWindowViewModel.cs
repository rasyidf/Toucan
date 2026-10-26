using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Plugins;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Services the main window view model depends on, grouped to keep the constructor readable.</summary>
public sealed record MainWindowServices(
    IRecentProjectService RecentProjects,
    IDialogService Dialogs,
    IAsyncMessageService Messages,
    IPreferenceService Preferences,
    IProjectService ProjectService,
    IProjectLifecycleService Lifecycle,
    ITranslationManagementService TranslationStore,
    ILanguageManagementService LanguageManagement,
    ITranslationStrategyFactory StrategyFactory,
    IPretranslationService Pretranslation,
    IProviderSettingsService ProviderSettings,
    IValidationPipeline Validation,
    ISourceCodeService SourceCode,
    ITranslationAnalyzer Analyzer,
    IUndoRedoService UndoRedo,
    IFuzzySearchService FuzzySearch,
    ISearchAndReplaceService SearchAndReplace,
    ITranslationMemory TranslationMemory,
    BulkOperationService BulkOperations,
    ITranslationProviderRegistry ProviderRegistry,
    IAiService Ai,
    IAiSettingsStore AiSettings,
    ISourceClarityService Clarity,
    IDiagnosticsService? Diagnostics = null,
    IPluginCatalog? PluginCatalog = null,
    IEnumerable<SpreadsheetDialect>? SpreadsheetDialects = null,
    IEnumerable<IProjectCompatibilityImporter>? CompatibilityImporters = null,
    SpellingService? Spelling = null, DictionaryRegistry? Dictionaries = null,
    INotificationCenter? Notifications = null, NotificationHistoryViewModel? History = null,
    IProjectDefaultsService? Defaults = null,
    WorkspaceService? Workspace = null);

/// <summary>
/// State and commands for the main editor window. Split across partial files by concern:
/// File (open/save/import/export), Nav (filtering, paging, modes), Edit (keys, languages, undo),
/// Translation (MT, validation, TM, source code), Search (find &amp; replace), and Bulk (multi-select).
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProjectDefaultsService? _defaultsService;
    private Dictionary<string, ValidationRuleConfig> _inlineRuleDefaults;
    private readonly IRecentProjectService _recentFileService;
    private readonly IDialogService _dialogService;
    private readonly IReadOnlyList<SpreadsheetDialect> _spreadsheetDialects;
    private readonly IReadOnlyList<IProjectCompatibilityImporter> _compatibilityImporters;
    private readonly IAsyncMessageService _messageService;
    private readonly IPreferenceService _preferenceService;
    private readonly IProjectService _projectService;
    private readonly IProjectLifecycleService _lifecycleService;
    private readonly IUndoRedoService _undoRedoService;
    private readonly ITranslationManagementService _translationStore;
    private readonly ILanguageManagementService _languageManagement;
    private readonly ITranslationStrategyFactory _strategyFactory;
    private readonly IPretranslationService _pretranslationService;
    private readonly IProviderSettingsService _providerSettingsService;
    private readonly ITranslationProviderRegistry _providerRegistry;
    private readonly IValidationPipeline _validationPipeline;
    private readonly ISourceCodeService _sourceCodeService;
    private readonly ITranslationAnalyzer _translationAnalyzer;
    private readonly IFuzzySearchService _fuzzySearch;
    private readonly ISearchAndReplaceService _searchAndReplace;
    private readonly ITranslationMemory _translationMemory;
    private readonly BulkOperationService _bulkOperations;
    private readonly IAiService _ai;
    private readonly IAiSettingsStore _aiSettings;
    private readonly ISourceClarityService _clarity;
    private readonly IDiagnosticsService? _diagnostics;
    private readonly IPluginCatalog? _pluginCatalog;
    private readonly SpellingService? _spellingService;
    private readonly DictionaryRegistry? _dictionaries;
    private SpellingSession? _spellingSession;
    internal WorkspaceService Workspace { get; }

    private readonly DispatcherTimer _searchDebounce;
    /// <summary>Pause between the last keystroke in the Search panel and the search itself.</summary>
    private readonly DispatcherTimer _panelSearchTimer;

    public MainWindowViewModel(MainWindowServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _notifications = services.Notifications ?? new NotificationCenter();
        NotificationHistory = services.History ?? new NotificationHistoryViewModel(_notifications,
            id => KeybindingService.Registry.ExecuteAsync(id));
        NotificationHistory.PropertyChanged += OnNotificationHistoryChanged;
        _spellingService = services.Spelling; _dictionaries = services.Dictionaries;
        _recentFileService = services.RecentProjects;
        _dialogService = services.Dialogs;
        _compatibilityImporters = (services.CompatibilityImporters ?? []).ToList();
        _spreadsheetDialects = new[] { ExcelService.Toucan }.Concat(services.SpreadsheetDialects ?? []).ToList();
        _messageService = services.Messages;
        _preferenceService = services.Preferences;
        _projectService = services.ProjectService;
        _lifecycleService = services.Lifecycle;
        _undoRedoService = services.UndoRedo;
        _translationStore = services.TranslationStore;
        _languageManagement = services.LanguageManagement;
        _strategyFactory = services.StrategyFactory;
        _pretranslationService = services.Pretranslation;
        _providerSettingsService = services.ProviderSettings;
        _providerRegistry = services.ProviderRegistry;
        _validationPipeline = services.Validation;
        Workspace = services.Workspace ?? new WorkspaceService(_translationStore, _validationPipeline);
        _sourceCodeService = services.SourceCode;
        _translationAnalyzer = services.Analyzer;
        _fuzzySearch = services.FuzzySearch;
        _searchAndReplace = services.SearchAndReplace;
        _translationMemory = services.TranslationMemory;
        _bulkOperations = services.BulkOperations;
        _ai = services.Ai;
        _aiSettings = services.AiSettings;
        _clarity = services.Clarity;
        _diagnostics = services.Diagnostics;
        _pluginCatalog = services.PluginCatalog;
        _defaultsService = services.Defaults;
        _inlineRuleDefaults = _defaultsService?.Load().ValidationRules ?? [];

        _translationStore.DirtyStateChanged += OnStoreDirtyStateChanged;

        appOptions = _preferenceService.Load();
        editorLayout = Enum.TryParse<EditorLayout>(appOptions.EditorLayout, out var savedLayout) && Enum.IsDefined(savedLayout)
            ? savedLayout : EditorLayout.Cards;
        InitAi();
        editorMode = PanelService.Instance.EditorMode;

        pagingController = new PaginationViewModel<LanguageGroupViewModel>(EffectivePageSize, [], EffectiveMaxItems);

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Search(SearchText);
        };

        _panelSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _panelSearchTimer.Tick += (_, _) =>
        {
            _panelSearchTimer.Stop();
            RunSearch();
        };

        RefreshProviderChoices();
        RefreshRecentProjects();
        LoadFilterHistory();
        PagedUpdates();
    }

    // ───────────────────────── Core state ─────────────────────────

    /// <summary>Working copy of every translation in the project (kept in sync with the translation store).</summary>
    [ObservableProperty] private List<TranslationItem> allTranslation = [];

    [ObservableProperty] private NsTreeItem? selectedNode;
    [ObservableProperty] private LanguageGroupViewModel? selectedGroup;
    [ObservableProperty] private SummaryInfoViewModel summaryInfo = new();
    [ObservableProperty] private PaginationViewModel<LanguageGroupViewModel> pagingController;
    [ObservableProperty] private ObservableCollection<NsTreeItem> currentTreeItems = [];
    [ObservableProperty] private ObservableCollection<NsFlatItem> currentFlatItems = [];
    [ObservableProperty] private ObservableCollection<string> selectedNodePath = [];
    [ObservableProperty] private AppOptions appOptions;
    [ObservableProperty] private ProjectSettings? projectSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject), nameof(WindowTitle), nameof(ProjectName))]
    private string currentPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isDirty;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool canCancelLoad;
    [ObservableProperty] private string loadDetail = string.Empty;
    private CancellationTokenSource? _openCts;
    [ObservableProperty] private bool isTreeView = true;
    [ObservableProperty] private bool showStartScreen = true;
    [ObservableProperty] private ObservableCollection<string> hiddenNamespaces = [];
    [ObservableProperty] private ObservableCollection<PaginationButton> pageButtons = [];
    [ObservableProperty] private IEnumerable<LanguageGroupViewModel> pageData = [];
    [ObservableProperty] private string pageMessage = string.Empty;
    [ObservableProperty] private int sessionDirtyCount;

    /// <summary>Keys modified in the current session (cleared on save).</summary>
    public HashSet<string> SessionDirtyKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasProject => !string.IsNullOrEmpty(CurrentPath);

    public string ProjectName => ProjectSettings?.Name is { Length: > 0 } name ? name : Path.GetFileName(CurrentPath.TrimEnd('/', '\\'));

    public string WindowTitle => HasProject ? $"{(IsDirty ? "● " : string.Empty)}{ProjectName} — Toucan" : "Toucan";

    private string? _primaryLanguage;

    /// <summary>The source language, matched against the languages actually present in the files.</summary>
    public string PrimaryLanguage
    {
        get => _primaryLanguage ?? ProjectSettings?.PrimaryLanguage ?? AppOptions.DefaultLanguage ?? "en-US";
        private set
        {
            _primaryLanguage = value;
            StatusBarService.Instance.UpdateDefaultLanguage(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Convenience for the start screen checkbox. Persists immediately.</summary>
    public bool OpenLastProjectOnStartup
    {
        get => AppOptions.OpenLastProjectOnStartup;
        set
        {
            if (AppOptions.OpenLastProjectOnStartup == value) return;
            AppOptions.OpenLastProjectOnStartup = value;
            _preferenceService.Save(AppOptions);
            OnPropertyChanged();
        }
    }

    private int EffectivePageSize => AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
    private int EffectiveMaxItems => AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;

    /// <summary>Raised when the view should move keyboard focus to the filter box.</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Raised when the view should toggle fullscreen (a window concern).</summary>
    public event EventHandler? FullscreenRequested;

    // ───────────────────────── Paging ─────────────────────────

    internal void PagedUpdates()
    {
        PageData = PagingController.PageData;
        PageMessage = PagingController.PageMessage;
        UpdatePageButtons(1);
        ApplyLanguageVisibility();
        FocusedIndex = Math.Clamp(FocusedIndex, 0, Math.Max(0, PagingController.Data.Count - 1));
        if (FocusedEditorMode || ZenMode) SelectedGroup = ZenCurrentItem;
        FocusedNextCommand.NotifyCanExecuteChanged();
        FocusedPreviousCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ZenCurrentItem));
        OnPropertyChanged(nameof(FocusedPositionText));
        OnPropertyChanged(nameof(IsEditorEmpty));
    }

    /// <summary>True when the current filter matches nothing (drives the empty state).</summary>
    public bool IsEditorEmpty => PagingController.Data.Count == 0;

    private void UpdatePageButtons(int window)
    {
        var buttons = new ObservableCollection<PaginationButton>();
        int pages = Math.Max(1, PagingController.Pages);
        int current = Math.Clamp(PagingController.Page, 1, pages);

        buttons.Add(new PaginationButton(1, false, current == 1));
        if (pages > 1)
        {
            int start = Math.Max(2, current - window);
            int end = Math.Min(pages - 1, current + window);
            if (start > 2) buttons.Add(new PaginationButton(0, true, false));
            for (int i = start; i <= end; i++) buttons.Add(new PaginationButton(i, false, i == current));
            if (end < pages - 1) buttons.Add(new PaginationButton(0, true, false));
            buttons.Add(new PaginationButton(pages, false, current == pages));
        }
        PageButtons = buttons;
    }

    /// <summary>Swaps the editor list, disposing the previous cards (which flushes in-flight edits).</summary>
    private void SetEditorGroups(List<LanguageGroupViewModel> groups, bool isPartial = false)
    {
        var selectedId = SelectedGroup?.EntryId;
        var old = PagingController.Data.ToList();
        PagingController.SwapData(groups, isPartial);
        foreach (var g in old) g.Dispose();
        MarkDirtyGroups();
        PagedUpdates();
        if (HasDocumentNavigation) SelectedGroup = groups.FirstOrDefault(g => g.EntryId == selectedId) ?? groups.FirstOrDefault();
    }

    // ───────────────────────── Tree & summary ─────────────────────────

    internal void RefreshTree()
    {
        RefreshDocumentNavigation();
        var parsable = AllTranslation.ForParse().Where(t => !IsNamespaceHidden(t.Namespace)).ToList();
        var nodes = AppOptions.PlainTextKeys ? parsable.ToNsTreeFlat() : parsable.ToNsTree();
        CurrentTreeItems = new ObservableCollection<NsTreeItem>(nodes);
        CurrentFlatItems = new ObservableCollection<NsFlatItem>(
            parsable.Select(t => t.Namespace).Distinct().OrderBy(n => n, StringComparer.Ordinal).Select(n => new NsFlatItem
            {
                DisplayKey = n,
                FullKey = n,
                IsLeaf = true,
                Source = new NsTreeItem { Name = n.Split('.').Last(), Namespace = n }
            }));
    }

    internal void UpdateSummaryInfo()
    {
        SummaryInfo.Update(AllTranslation, PrimaryLanguage);
        var parsable = AllTranslation.ForParse().ToList();
        var total = parsable.Count;
        var translated = parsable.Count(t => !string.IsNullOrEmpty(t.Value));
        var errors = ValidationIssues.Count(i => i.Severity == ValidationSeverity.Error);
        var warnings = ValidationIssues.Count(i => i.Severity == ValidationSeverity.Warning);
        StatusBarService.Instance.UpdateStatistics(total, translated, errors, warnings, SummaryInfo.Details);
        StatusBarService.Instance.ViewModel?.Notifications.UpdateSummary(SummaryInfo.Details.Sum(d => d.Empty),
            ValidationIssues.Count(i => i.Severity == ValidationSeverity.Error),
            ValidationIssues.Count(i => i.Severity == ValidationSeverity.Warning),
            ValidationIssues.Count(i => i.Severity == ValidationSeverity.Info));
        StatusBarService.Instance.ViewModel?.Notifications.Update(NotificationHistory.Count);
    }

    // ───────────────────────── Dirty tracking ─────────────────────────

    private void OnStoreDirtyStateChanged(object? sender, bool dirty)
    {
        // The store raises this from a timer thread. Re-read the state when the post runs,
        // since several transitions may have been queued in between.
        Dispatcher.UIThread.Post(ApplyStoreDirtyState);
    }

    private void ApplyStoreDirtyState()
    {
        if (_translationStore.IsDirty || Workspace.IsDirty)
        {
            foreach (var item in _translationStore.GetDirtyItems())
            {
                if (!string.IsNullOrEmpty(item.Namespace)) SessionDirtyKeys.Add(item.Namespace);
            }
            SessionDirtyCount = SessionDirtyKeys.Count;
            MarkDirtyGroups();
            IsDirty = true;
        }
        else if (!_hasUntrackedChanges)
        {
            IsDirty = false;
        }
    }

    /// <summary>
    /// True when something changed that the store's value/comment baselines can't see
    /// (approvals, key renames of already-clean items). Cleared on save.
    /// </summary>
    private bool _hasUntrackedChanges;

    /// <summary>
    /// Called after the working copy gained or lost items (new key, delete, import, add language).
    /// The lifecycle service saves from the translation store, so the store must see the same item set.
    /// </summary>
    private void SyncStore()
    {
        var store = _translationStore.Translations;
        var storeSet = new HashSet<TranslationItem>(store, ReferenceEqualityComparer.Instance);
        var working = new HashSet<TranslationItem>(AllTranslation, ReferenceEqualityComparer.Instance);

        var added = AllTranslation.Where(t => !storeSet.Contains(t)).ToList();
        if (store.Any(t => !working.Contains(t)))
            _translationStore.RemoveItems(t => !working.Contains(t));
        if (added.Count > 0)
            _translationStore.AddItems(added);
    }

    /// <summary>Records a structural edit: syncs the store and flags the project dirty.</summary>
    private void MarkStructureChanged(IEnumerable<string>? keys = null)
    {
        SyncStore();
        foreach (var k in keys ?? []) if (!string.IsNullOrEmpty(k)) SessionDirtyKeys.Add(k);
        SessionDirtyCount = SessionDirtyKeys.Count;
        _hasUntrackedChanges = true;
        IsDirty = true;
        BumpWorkspaceRevision();
    }

    /// <summary>Records edits made directly on model objects (bulk operations, approvals, undo).</summary>
    private void NotifyBulkValueChanges(IEnumerable<TranslationItem> items)
    {
        foreach (var item in items)
        {
            _translationStore.NotifyValueChanged(item, item.Value);
            if (!string.IsNullOrEmpty(item.Namespace)) SessionDirtyKeys.Add(item.Namespace);
        }
        SessionDirtyCount = SessionDirtyKeys.Count;
        _hasUntrackedChanges = true;
        IsDirty = true;
        MarkDirtyGroups();
        BumpWorkspaceRevision();
    }

    private void MarkDirtyGroups()
    {
        foreach (var g in PagingController.Data)
        {
            var dirty = SessionDirtyKeys.Contains(g.Namespace)
                || g.PluralVariants.Any(v => SessionDirtyKeys.Contains(v.FullNamespace));
            if (g.IsDirty != dirty) g.IsDirty = dirty;
        }
    }

    private void ClearSessionDirtyState()
    {
        SessionDirtyKeys.Clear();
        SessionDirtyCount = 0;
        _hasUntrackedChanges = false;
        IsDirty = _translationStore.IsDirty || Workspace.IsDirty;
        MarkDirtyGroups();
    }

    partial void OnSessionDirtyCountChanged(int value) => StatusBarService.Instance.UpdateSessionDirtyCount(value);

    partial void OnStatusTextChanged(string value) => StatusBarService.Instance.UpdateStatus(value);

    partial void OnIsLoadingChanged(bool value) => StatusBarService.Instance.SetLoading(value);

    /// <summary>Callback from a translation row after a (debounced) edit or approval toggle.</summary>
    private void OnTranslationItemChanged(TranslationItemViewModel item)
    {
        if (!string.IsNullOrEmpty(item.Namespace)) SessionDirtyKeys.Add(item.Namespace);
        SessionDirtyCount = SessionDirtyKeys.Count;
        BumpWorkspaceRevision();
        if (!_translationStore.IsItemDirty(item.Model))
        {
            // Approval-only changes are invisible to the store's baselines.
            _hasUntrackedChanges = true;
        }
        IsDirty = true;
        MarkDirtyGroups();
        UpdateSummaryInfo();
        if (ReferenceEquals(item, FocusedTranslationItem)) UpdateGhostSuggestion(item);
    }

    internal TranslationItemViewModel CreateItemViewModel(TranslationItem item)
    {
        var resource = ProjectSettings?.ResourceDocuments.FirstOrDefault(d => d.Resource.Id == item.Identity?.ResourceId)?.Resource;
        return new(item, _undoRedoService, _translationStore, OnTranslationItemChanged, CanApprove, _spellingSession, ValidateInlineItem)
            { ResourcePath = resource?.RelativePath ?? "", IsArbResource = resource?.FormatId == FormatIds.Arb,
              IsSource = string.Equals(item.Language, PrimaryLanguage, StringComparison.OrdinalIgnoreCase) };
    }

    private IReadOnlyList<ValidationResult> ValidateInlineItem(TranslationItemViewModel item)
    {
        var id = Workspace.GetProjectionEntryId(item.Model);
        bool SameEntry(TranslationItem model) => HasDocumentNavigation ? Workspace.GetProjectionEntryId(model) == id : model.Namespace == item.Namespace;
        var live = PagingController.Data.SelectMany(g => g.AllItems).Where(t => SameEntry(t.Model))
            .GroupBy(t => t.Language).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        live[item.Language] = item.Value;
        if (item.SourceText is not null) live[PrimaryLanguage] = item.SourceText;
        var snapshots = AllTranslation.Where(SameEntry).Select(t => new TranslationItem
        {
            Namespace = t.Namespace, Language = t.Language, Value = live.GetValueOrDefault(t.Language, t.Value),
            Comment = t.Comment, IsApproved = t.IsApproved, FormatData = t.FormatData,
            ChangeType = t.ChangeType, LastModifiedUtc = t.LastModifiedUtc, ApprovedAtUtc = t.ApprovedAtUtc
        }).ToList();
        var enabledRules = _validationPipeline.Rules
            .Where(r => InlineRuleConfig(r.Id)?.Enabled != false)
            .Where(r => r.Id != "placeholder-mismatch" || PanelService.Instance.ShowPlaceholderWarnings)
            .Select(r => r.Id).ToArray();
        var results = _validationPipeline.Run(new ValidationContext { Items = snapshots, PrimaryLanguage = PrimaryLanguage }, enabledRules)
            .Where(r => r.Language is null || r.Language == item.Language)
            .Select(r => InlineRuleConfig(r.RuleId) is { } config &&
                Enum.TryParse<ValidationSeverity>(config.Severity, true, out var severity) ? r with { Severity = severity } : r)
            .OrderBy(r => r.Severity).ToList();
        if (results.Any(r => r.RuleId == "missing-translation")) results.RemoveAll(r => r.RuleId == "empty-value");
        return results;
    }

    private ValidationRuleConfig? InlineRuleConfig(string id) =>
        ProjectSettings?.ValidationRules?.GetValueOrDefault(id) ?? _inlineRuleDefaults.GetValueOrDefault(id);

    internal LanguageGroupViewModel CreateGroup(string ns, Guid? entryId = null, TranslationPath? namespacePath = null, TranslationPath? keyPath = null)
    {
        var group = new LanguageGroupViewModel(ns, CreateItemViewModel, PrimaryLanguage)
        {
            EntryId = entryId,
            StructuredName = keyPath?.Segments.LastOrDefault(),
            StructuredParent = namespacePath is null || keyPath is null ? null : string.Join('.', namespacePath.Segments.Concat(keyPath.Segments.SkipLast(1))),
            SourceOrigins = entryId is null || Workspace.Source is not { } source ? string.Empty : string.Join(", ",
                source.Units.Where(u => u.EntryId.Value == entryId).Select(u => source.Documents.Single(d => d.Id == u.Binding.DocumentId).RelativePath + "#" + u.Binding.Location.EscapedPath)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        };
        group.TranslateRequested += (s, e) =>
        {
            if (s is LanguageGroupViewModel g) { SelectedGroup = g; _ = TranslateKeyAsync(g.Namespace); }
        };
        return group;
    }

    /// <summary>Pushes any debounced edits in the visible editor to the model.</summary>
    internal void FlushPendingEdits()
    {
        foreach (var g in PagingController.PageData) g.FlushEdits();
        ZenCurrentItem?.FlushEdits();
    }

    // ───────────────────────── Help ─────────────────────────

    [RelayCommand]
    private static void HelpHomepage() => PlatformService.OpenUrl("https://toucan.rasyid.dev");

    [RelayCommand]
    private static void ReportIssue() => PlatformService.OpenUrl("https://github.com/rasyidf/Toucan/issues");

    /// <summary>Puts a report on the clipboard that is safe to paste into a bug report: versions, plugins and their recent logs, with secrets masked.</summary>
    [RelayCommand]
    private async Task CopyDiagnostics()
    {
        if (_diagnostics is null) return;
        await PlatformService.SetClipboardTextAsync(_diagnostics.BuildReport(_pluginCatalog));
        StatusText = Locales.Loc.T("Diagnostics copied to the clipboard.");
    }

    [RelayCommand]
    private async Task HelpAbout() => await ShowPreferencesAtAsync(OptionsViewModel.Pages.Count - 1);

    [RelayCommand]
    private Task ShowPreferences() => ShowPreferencesAtAsync(0);

    [RelayCommand]
    private Task ShowPlugins() => ShowPreferencesAtAsync(OptionsViewModel.PluginsPage);

    private async Task ShowPreferencesAtAsync(int page)
    {
        var updated = await _dialogService.ShowOptionsAsync(page);
        if (updated == null) return;

        var previousLanguage = AppOptions.AppLanguage;
        var plainKeysChanged = AppOptions.PlainTextKeys != updated.PlainTextKeys;
        AppOptions = updated;
        _inlineRuleDefaults = _defaultsService?.Load().ValidationRules ?? [];
        ThemeService.Apply(AppOptions.Theme);
        ColorSchemeService.Apply(AppOptions);
        ThemeService.ApplyFontSize(AppOptions.FontSize);
        PanelService.Instance.ShowPlaceholderWarnings = AppOptions.ShowPlaceholderWarnings;
        foreach (var row in PagingController.Data.SelectMany(g => g.AllItems)) row.RefreshPlaceholderWarning();

        if (!string.Equals(previousLanguage, AppOptions.AppLanguage, StringComparison.OrdinalIgnoreCase))
        {
            await _messageService.ShowMessageAsync("The interface language will change after you restart Toucan.", "Restart Required");
        }

        var oldPage = PagingController.Page;
        var data = PagingController.Data.ToList();
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(InfiniteScroll ? int.MaxValue : EffectivePageSize, data, EffectiveMaxItems);
        PagingController.GoTo(oldPage);
        if (plainKeysChanged) RefreshTree();
        PagedUpdates();
    }

    [RelayCommand]
    private async Task ShowProjectProperties()
    {
        if (!HasProject) return;
        var settings = ProjectSettings ?? ProjectSettings.LoadFrom(CurrentPath) ?? ProjectSettings.CreateDefault(CurrentPath);
        var previousPrimary = settings.PrimaryLanguage;
        FlushPendingEdits();
        var revision = _workspaceRevision;
        var before = ProjectConfiguration.Copy(settings);
        before.ExpectedResourceFiles = settings.ResourceDocuments.ToDictionary(d => ResourceFilePath.Resolve(settings.ProjectPath, d.Resource.RelativePath), d => ResourceConfigurationPlanner.Fingerprint(ResourceFilePath.Resolve(settings.ProjectPath, d.Resource.RelativePath)));
        var staged = ProjectConfiguration.Copy(settings);
        staged.IsDirty = IsDirty;
        var result = await _dialogService.ShowProjectPropertiesAsync(staged, OrderedLanguages().ToList());
        if (result == null) return;

        if (revision != _workspaceRevision || !ReferenceEquals(settings, ProjectSettings))
        {
            await _messageService.ShowMessageAsync("The workspace changed while properties were open. Reopen properties to apply these changes.", "Project Properties");
            return;
        }
        var plan = result.ResourceSettings?.AcceptedPlan;
        if (plan != null && plan.DestinationHashes.Concat(plan.ExternalHashes).Any(p => ResourceConfigurationPlanner.Fingerprint(p.Key) != p.Value))
        {
            await _messageService.ShowMessageAsync("Resource files changed while properties were open. Review a new preview.", "Project Properties");
            return;
        }
        if (plan != null) staged.ExpectedResourceFiles = new(plan.DestinationHashes);
        using (_undoRedoService.BeginGroup())
        {
            _undoRedoService.RecordConfiguration(before, staged);
            if (plan != null) { _undoRedoService.RecordStructure(CaptureStructure(), plan.Entries); AllTranslation = plan.Entries.Select(i => ResourceEntryOperations.Copy(i)).ToList(); }
            ProjectConfiguration.Apply(settings, staged);
        }
        if (plan != null) settings.ExpectedResourceFiles = new(plan.DestinationHashes);
        _translationStore.NotifyConfigurationChanged(true);
        MarkStructureChanged();
        ProjectSettings = settings;
        _spellingSession?.Refresh();
        HiddenNamespaces = new ObservableCollection<string>(settings.HiddenNamespaces ?? []);
        StatusBarService.Instance.UpdateProjectName(ProjectName);
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CopyTemplates));

        if (!string.Equals(previousPrimary, settings.PrimaryLanguage, StringComparison.Ordinal))
            SetPrimaryLanguage(settings.PrimaryLanguage);
        else
        {
            RefreshTree();
            Search(SearchText, true);
        }

        if (result.ManageLanguagesRequested) await ManageLanguages();
    }

    [RelayCommand]
    private void Exit() => _dialogService.Shutdown();

    public void Dispose()
    {
        NotificationHistory.PropertyChanged -= OnNotificationHistoryChanged;
        NotificationHistory.Dispose();
        if (FocusedTranslationItem != null) FocusedTranslationItem.PropertyChanged -= OnFocusedItemPropertyChanged;
        _spellingValidation?.Cancel();
        _spellingSession?.Dispose();
        _translationStore.DirtyStateChanged -= OnStoreDirtyStateChanged;
        _searchDebounce.Stop();
        _panelSearchTimer.Stop();
        _sourceScanCancellation?.Cancel();
        _sourceScanCancellation?.Dispose();
        _openCts?.Cancel();
        _openCts?.Dispose();
        foreach (var g in PagingController.Data) g.Dispose();
        GC.SuppressFinalize(this);
    }
}
