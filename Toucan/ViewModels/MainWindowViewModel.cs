using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Extensions;
using Toucan.Models;
using Toucan.Services;
using Toucan.Views.Dialogs;

namespace Toucan.ViewModels;

internal partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private List<TranslationItem> allTranslation = [];

    [ObservableProperty]
    private NsTreeItem? selectedNode;

    [ObservableProperty]
    private SummaryInfoViewModel summaryInfo = new();

    [ObservableProperty]
    private PaginationViewModel<LanguageGroupViewModel> pagingController;

    [ObservableProperty]
    private ObservableCollection<NsTreeItem> currentTreeItems = [];


    [ObservableProperty]
    private AppOptions appOptions;

    [ObservableProperty]
    private string currentPath = string.Empty;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

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
    private ObservableCollection<NsTreeItem> selectedNodes = [];


    [ObservableProperty]
    private IEnumerable<LanguageGroupViewModel> pageData = [];

    [ObservableProperty]
    private string pageMessage = string.Empty;

    [ObservableProperty]
    private int paginationWindow = 1; // show 1 page on each side = 3 items centered

    // Page button model for UI - moved to shared model Toucan.Models.PaginationButton

    [ObservableProperty]
    private ObservableCollection<PaginationButton> pageButtons = [];

    private readonly IRecentProjectService _recentFileService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly IPreferenceService _preferenceService;
    private readonly IBulkActionService? _bulkActionService;
    private readonly IPretranslationService? _pretranslationService;
    private readonly Func<string, LanguageGroupViewModel>? _languageGroupFactory;
    private readonly Func<IEnumerable<string>, IEnumerable<TranslationItem>, IPretranslationService, PreTranslateViewModel>? _preTranslateFactory;
    private readonly IProjectService? _projectService;
    private readonly ITranslationStrategyFactory? _strategyFactory;
    private readonly Func<NewProjectPrompt>? _newProjectPromptFactory;
    private readonly IValidationPipeline? _validationPipeline;
    private readonly ISourceCodeService? _sourceCodeService;
    private readonly ITranslationAnalyzer? _translationAnalyzer;
    private readonly IProviderSettingsService? _providerSettingsService;
    private readonly IUndoRedoService? _undoRedoService;
    private readonly ITranslationManagementService? _translationManagement;
    private readonly ILanguageManagementService? _languageManagementService;
    private readonly IProjectLifecycleService? _lifecycleService;


    public MainWindowViewModel(
        IRecentProjectService recentFileService,
        IDialogService dialogService,
        IMessageService messageService,
        IPreferenceService preferenceService,
        IBulkActionService? bulkActionService = null,
        IPretranslationService? pretranslationService = null,
        IProjectService? projectService = null,
        ITranslationStrategyFactory? strategyFactory = null,
        Func<string, LanguageGroupViewModel>? languageGroupFactory = null,
        Func<IEnumerable<string>, IEnumerable<TranslationItem>, IPretranslationService, PreTranslateViewModel>? preTranslateFactory = null,
        IValidationPipeline? validationPipeline = null,
        ISourceCodeService? sourceCodeService = null,
        ITranslationAnalyzer? translationAnalyzer = null,
        IProviderSettingsService? providerSettingsService = null,
        IUndoRedoService? undoRedoService = null,
        ILanguageManagementService? languageManagementService = null,
        IProjectLifecycleService? lifecycleService = null,
        ITranslationManagementService? translationManagement = null)
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
        _providerSettingsService = providerSettingsService;
        _undoRedoService = undoRedoService;
        _languageManagementService = languageManagementService;
        _lifecycleService = lifecycleService;
        _translationManagement = translationManagement;

        // Subscribe to dirty state changes from the translation management service
        if (_translationManagement != null)
        {
            _translationManagement.DirtyStateChanged += OnTranslationDirtyStateChanged;
        }

        AppOptions = _preferenceService.Load();

        // Initialize PagingController after loading options
        int pageSize = AppOptions.PageSize <= 0 ? 30 : AppOptions.PageSize;
        int maxItems = AppOptions.MaxItems <= 0 ? 100 : AppOptions.MaxItems;
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, new List<LanguageGroupViewModel>(), maxItems);
        PagedUpdates();
    }



    [RelayCommand]
    private void HelpHomepage()
    {
        // Redirect to Rasyid.dev
        OpenUrl("https://rasyid.dev");
    }


    [RelayCommand]
    private void HelpAbout()
    {
        _ = _dialogService.ShowAbout();
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
        {
            return;
        }

        if (PagingController == null)
        {
            return;
        }

        int pages = Math.Max(1, PagingController.Pages);
        int current = Math.Min(Math.Max(1, PagingController.Page), pages);

        if (pages <= 0)
        {
            return;
        }

        // always show first page
        PageButtons.Add(new PaginationButton(1, default, current == 1));

        if (pages == 1)
        {
            return;
        }

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



    internal static void OpenUrl(string url)
    {
        try
        {
            _ = Process.Start(url);
        }
        catch
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&", StringComparison.InvariantCulture);
                _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _ = Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _ = Process.Start("open", url);
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

        // Replace the collection reference to trigger converter re-evaluation (flat list binding)
        CurrentTreeItems = new ObservableCollection<NsTreeItem>(nodes);
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
        var currentData = PagingController?.Data ?? [];
        PagingController = new PaginationViewModel<LanguageGroupViewModel>(pageSize, currentData, maxItems);
        PagingController.Page = Math.Min(Math.Max(1, oldPage), PagingController.Pages);
        PagedUpdates();
    }

    /// <summary>
    /// Exposes the dirty state from ITranslationManagementService for UI binding.
    /// This is kept in sync via the DirtyStateChanged subscription.
    /// </summary>
    public bool HasUnsavedChanges => _translationManagement?.IsDirty ?? IsDirty;

    private void OnTranslationDirtyStateChanged(object? sender, bool isDirty)
    {
        IsDirty = isDirty;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }
}
