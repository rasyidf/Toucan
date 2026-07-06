using System.Windows;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Services;
using Toucan.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Toucan;


public partial class MainWindow : FluentWindow
{
    internal MainWindowViewModel ViewModel { get; }
    private readonly Toucan.Core.Contracts.Services.IFileWatcherService _fileWatcher;
    private readonly Toucan.Core.Contracts.Services.IProjectLifecycleService? _lifecycleService;

    internal MainWindow(string startupPath, MainWindowViewModel viewModel, StatusBarViewModel statusViewModel, Toucan.Core.Contracts.Services.IFileWatcherService fileWatcher, Toucan.Core.Contracts.Services.IProjectLifecycleService? lifecycleService = null)
    {
        _fileWatcher = fileWatcher;
        _lifecycleService = lifecycleService;

        InitializeComponent();

        ViewModel = viewModel;
        DataContext = ViewModel;
        Services.KeybindingService.Apply(this, ViewModel);
        PreviewKeyDown += (s, e) => Services.KeybindingService.HandleZenKeys(e, ViewModel);
        ViewModel.FullscreenRequested = ToggleFullscreen;
        UpdateStartupOptions(startupPath);

        // Wire side panel content switching
        var sideRegistry = Toucan.Core.Services.SidePanelRegistry.Instance;
        sideRegistry.PropertyChanged += (s, ea) =>
        {
            if (ea.PropertyName == nameof(sideRegistry.ActiveLeftPanel))
                UpdateLeftPanelContent(sideRegistry.ActiveLeftPanel?.Id);
            else if (ea.PropertyName == nameof(sideRegistry.ActiveRightPanel))
                UpdateRightPanelContent(sideRegistry.ActiveRightPanel?.Id);
            else if (ea.PropertyName == nameof(sideRegistry.LeftSlotVisible))
                sidebarColumn.Width = sideRegistry.LeftSlotVisible ? new GridLength(320) : new GridLength(0);
            else if (ea.PropertyName == nameof(sideRegistry.RightSlotVisible))
                inspectorColumn.Width = sideRegistry.RightSlotVisible ? new GridLength(280) : new GridLength(0);
        };
        Services.PanelService.Instance.PropertyChanged += (s, ea) =>
        {
            if (ea.PropertyName == nameof(Services.PanelService.ZenMode))
            {
                // Zen mode collapses both; exiting restores both
                if (Services.PanelService.Instance.ZenMode)
                {
                    sidebarColumn.Width = new GridLength(0);
                    inspectorColumn.Width = new GridLength(0);
                }
                // ExitZenMode sets LeftSlotVisible/RightSlotVisible=true which triggers the above
            }
        };
        // Apply saved panel state on startup
        if (!sideRegistry.LeftSlotVisible)
            sidebarColumn.Width = new GridLength(0);
        if (!sideRegistry.RightSlotVisible)
            inspectorColumn.Width = new GridLength(0);
        // Set initial content
        UpdateLeftPanelContent(sideRegistry.ActiveLeftPanel?.Id);
        UpdateRightPanelContent(sideRegistry.ActiveRightPanel?.Id);

        ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

        // wire up statusbar view model and service
        // initialize from main view model
        statusViewModel.StatusText = ViewModel.StatusText;
        statusViewModel.IsLoading = ViewModel.IsLoading;
        statusViewModel.ProjectName = System.IO.Path.GetFileName(ViewModel.CurrentPath) ?? ViewModel.CurrentPath ?? "Toucan Project";
        // show configured default language (app preference)
        statusViewModel.DefaultLanguage = ViewModel.AppOptions?.DefaultLanguage ?? "en-US";
        StatusBarService.Instance.Register(statusViewModel);
        RootStatusBar.DataContext = statusViewModel;

        // Wire project panel click to open project properties
        statusViewModel.Project.ProjectPropertiesRequested += (_, _) =>
        {
            if (ViewModel.ShowProjectPropertiesCommand.CanExecute(null))
                ViewModel.ShowProjectPropertiesCommand.Execute(null);
        };

        // Wire mode panel click to cycle editor mode
        statusViewModel.Mode.ModeCycleRequested += (_, _) =>
        {
            ViewModel.EditorMode = ViewModel.EditorMode switch
            {
                ViewModels.EditorMode.Editor => ViewModels.EditorMode.Review,
                ViewModels.EditorMode.Review => ViewModels.EditorMode.Audit,
                _ => ViewModels.EditorMode.Editor
            };
            statusViewModel.Mode.Update(ViewModel.EditorMode.ToString());
        };

        // Wire VCS panel click to focus source control side panel
        statusViewModel.Vcs.DetailsRequested += (_, _) =>
        {
            Core.Services.SidePanelRegistry.Instance.Activate("source-control");
        };

        // Wire stats panel click to run validation (shows issues panel)
        statusViewModel.Stats.StatisticsRequested += (_, _) =>
        {
            if (ViewModel.RunValidationCommand.CanExecute(null))
                ViewModel.RunValidationCommand.Execute(null);
        };

        // Wire language panel selection to update primary language in project settings
        statusViewModel.Language.LanguageChanged += lang =>
        {
            if (string.IsNullOrEmpty(ViewModel.CurrentPath)) return;
            var settings = Core.Models.ProjectSettings.LoadFrom(ViewModel.CurrentPath);
            if (settings is not null)
            {
                settings.PrimaryLanguage = lang;
                settings.Save();
            }
            StatusBarService.Instance.UpdateDefaultLanguage(lang);
            // Refresh editor to reorder languages (primary first)
            ViewModel.RefreshTree();
        };

        // forward basic updates from the main VM to the status bar
        ViewModel.PropertyChanged += (s, ea) =>
        {
            if (ea.PropertyName == nameof(MainWindowViewModel.StatusText))
            {
                statusViewModel.StatusText = ViewModel.StatusText;
            }
            else if (ea.PropertyName == nameof(MainWindowViewModel.IsLoading))
            {
                statusViewModel.IsLoading = ViewModel.IsLoading;
            }
            else if (ea.PropertyName == nameof(MainWindowViewModel.CurrentPath))
            {
                statusViewModel.ProjectName = System.IO.Path.GetFileName(ViewModel.CurrentPath) ?? ViewModel.CurrentPath ?? "Toucan Project";
                if (!string.IsNullOrEmpty(ViewModel.CurrentPath))
                {
                    _fileWatcher.Watch(ViewModel.CurrentPath);
                }
            }
        };
        // ponytail: FilesChanged now handled by IExternalChangeHandler wired to the lifecycle service
        ViewModel.PagedUpdates();
    }

    private bool _hasExplicitStartupPath;

    private void UpdateStartupOptions(string startupPath)
    {
        ViewModel.AppOptions = AppOptions.LoadFromDisk();

        if (!string.IsNullOrEmpty(startupPath))
        {
            // Explicit path from command-line — always open
            _hasExplicitStartupPath = true;
            ViewModel.CurrentPath = startupPath;
            ViewModel.AppOptions.LastProjectPath = ViewModel.CurrentPath;
            ViewModel.AppOptions.ToDisk();
        }
        else if (ViewModel.AppOptions.OpenLastProjectOnStartup)
        {
            // Auto-open last project if setting is enabled
            ViewModel.CurrentPath = ViewModel.AppOptions.LastProjectPath ?? string.Empty;
        }
        else
        {
            // Show start screen
            ViewModel.CurrentPath = string.Empty;
        }
    }

    private void UpdateLeftPanelContent(string? panelId)
    {
        leftPanelHost.PanelContent = panelId switch
        {
            "explorer" => CreateExplorerPanel(),
            "source-code" => new Views.Panels.SourceCodePanel(),
            "search" => new Views.Panels.SearchPanel(),
            "issues" => new Views.Panels.IssuesPanel(),
            "source-control" => new Views.Panels.SourceControlPanel(),
            _ => null!
        };
        leftPanelHost.PanelActions = panelId switch
        {
            "explorer" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.TextBulletListTree20, "Toggle view mode", ViewModel.ToggleViewModeCommand),
                (Wpf.Ui.Controls.SymbolRegular.Add16, "New Item", ViewModel.NewItemCommand)),
            "source-code" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.ArrowSync20, "Scan source code", ViewModel.ScanSourceCodeCommand),
                (Wpf.Ui.Controls.SymbolRegular.Settings16, "Source settings", ViewModel.ShowPreferencesCommand)),
            "issues" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.ArrowSync20, "Run validation", ViewModel.RunValidationCommand)),
            _ => null!
        };
    }

    private void UpdateRightPanelContent(string? panelId)
    {
        rightPanelHost.PanelContent = panelId switch
        {
            "languages" => new Views.Panels.LanguagesPanel(),
            "inspector" => new Views.Panels.InspectorPanel(),
            "machine-translation" => new Views.Panels.MachineTranslationPanel(),
            "translation-memory" => new Views.Panels.TranslationMemoryPanel(),
            "dictionary" => new Views.Panels.DictionaryPanel(),
            _ => null!
        };
        rightPanelHost.PanelActions = panelId switch
        {
            "languages" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.ChevronDown20, "Expand/Collapse All", ViewModel.SummaryInfo.ToggleExpandAllCommand),
                (Wpf.Ui.Controls.SymbolRegular.Settings16, "Manage Languages", ViewModel.ManageLanguagesCommand)),
            "machine-translation" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.Translate20, "Pre-translate", ViewModel.PreTranslateBulkCommand),
                (Wpf.Ui.Controls.SymbolRegular.Add16, "Translate selected key", ViewModel.TranslateSelectedKeyCommand)),
            "translation-memory" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.Delete20, "Clear Translation Memory", ViewModel.ClearTmCommand),
                (Wpf.Ui.Controls.SymbolRegular.ArrowExportLtr20, "Export TMX", ViewModel.ExportTmxCommand),
                (Wpf.Ui.Controls.SymbolRegular.ArrowImport20, "Import TMX", ViewModel.ImportTmxCommand)),
            "dictionary" => BuildActions(
                (Wpf.Ui.Controls.SymbolRegular.Add16, "New term", ViewModel.AddDictionaryTermCommand)),
            _ => null!
        };
    }

    private static System.Windows.Controls.StackPanel BuildActions(params (Wpf.Ui.Controls.SymbolRegular Icon, string Tooltip, System.Windows.Input.ICommand Cmd)[] items)
    {
        var sp = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        foreach (var (icon, tooltip, cmd) in items)
        {
            var btn = new Wpf.Ui.Controls.Button
            {
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                Padding = new Thickness(4, 4, 4, 4),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = tooltip,
                Command = cmd,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon, FontSize = 14 }
            };
            sp.Children.Add(btn);
        }
        return sp;
    }

    private Views.Panels.ExplorerPanel CreateExplorerPanel()
    {
        var panel = new Views.Panels.ExplorerPanel();
        var rv = panel.ResourcesContent;
        rv.SelectionChanged += TreeNamespace_SelectedItemChanged;
        rv.ListSelectionChanged += ResourcesView_ListSelectionChanged;
        return panel;
    }

    private void TreeNamespace_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = (NsTreeItem)e.NewValue;
        if (ViewModel.SelectedNode == null)
        {
            return;
        }

        ViewModel.SelectedNode.IsExpanded = true;

        string clickedNamespace = ViewModel.SelectedNode.Namespace;
        if (ViewModel.SelectedNode.HasItems)
        {
            clickedNamespace += ".";
        }

        if (!string.IsNullOrWhiteSpace(clickedNamespace))
        {
            ViewModel.SearchText = clickedNamespace;
        }
    }

    private void ResourcesView_ListSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView lv && lv.SelectedItem is NsFlatItem flat)
        {
            // try to select the corresponding node
            ViewModel.SelectedNode = flat.Source;
            if (ViewModel.SelectedNode != null)
            {
                ViewModel.SelectedNode.IsExpanded = true;
                string clickedNamespace = ViewModel.SelectedNode.Namespace;
                if (ViewModel.SelectedNode.HasItems)
                {
                    clickedNamespace += ".";
                }

                if (!string.IsNullOrWhiteSpace(clickedNamespace))
                {
                    ViewModel.SearchText = clickedNamespace;
                }
            }
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Open project if a path was set (either explicit arg or auto-open last)
        if (!string.IsNullOrEmpty(ViewModel.CurrentPath))
        {
            var path = ViewModel.CurrentPath;

            // If a file was passed (e.g. .tproj), resolve to its directory
            if (System.IO.File.Exists(path))
            {
                path = System.IO.Path.GetDirectoryName(path) ?? path;
                ViewModel.CurrentPath = path;
            }

            if (System.IO.Directory.Exists(path))
            {
                if (ViewModel.OpenRecentProjectCommand.CanExecute(path))
                {
                    await ViewModel.OpenRecentProjectCommand.ExecuteAsync(path).ConfigureAwait(true);
                }
            }
        }

        // Populate recent projects for flyout menu
        ViewModel.RefreshRecentProjects();
        ViewModel.LoadFilterHistory();

        SystemThemeWatcher.Watch(this, WindowBackdropType.Tabbed, true);
    }

    private void NextPage(object sender, RoutedEventArgs e)
    {
        ViewModel.NextPageCommand.Execute(null);
    }

    private void PreviousPage(object sender, RoutedEventArgs e)
    {
        ViewModel.PreviousPageCommand.Execute(null);
    }

    private void FirstPage(object sender, RoutedEventArgs e)
    {
        ViewModel.FirstPageCommand.Execute(null);
    }

    private void LastPage(object sender, RoutedEventArgs e)
    {
        ViewModel.LastPageCommand.Execute(null);
    }

    private void UpdateLanguageValue(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateSummaryInfo();
        ViewModel.IsDirty = true;
    }

    private void ShowAll(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowAll(ViewModel.SearchText);
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Delegate unsaved-changes prompt to the lifecycle service (which uses IUnsavedChangesHandler)
        if (_lifecycleService is not null && _lifecycleService.IsProjectOpen)
        {
            e.Cancel = true;
            var result = await _lifecycleService.CloseProjectAsync();
            if (result == Toucan.Core.Contracts.Services.CloseResult.Cancelled)
                return;

            // Close succeeded — clean up and shut down
            PanelService.Instance.SaveLayout();
            StatusBarService.Instance.Unregister();
            _fileWatcher.Stop();
            ViewModel.Cleanup();
            Application.Current.Shutdown();
            return;
        }

        PanelService.Instance.SaveLayout();
        StatusBarService.Instance.Unregister();
        _fileWatcher.Stop();
        ViewModel.Cleanup();
    }

    /// <summary>Toggle between maximized (fullscreen) and normal window state.</summary>
    internal void ToggleFullscreen()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }
}
