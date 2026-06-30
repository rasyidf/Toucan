using System.Windows;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;
using Toucan.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Toucan;


public partial class MainWindow : FluentWindow
{
    internal MainWindowViewModel ViewModel { get; }
    private readonly FileWatcherService _fileWatcher = new();

    internal MainWindow(string startupPath, MainWindowViewModel viewModel, StatusBarViewModel statusViewModel)
    {

        InitializeComponent();

        // ponytail: set icon via pack URI to avoid BAML TypeConverter error
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new System.Uri("pack://application:,,,/Assets/Images/WindowIcon.ico"));

        ViewModel = viewModel;
        DataContext = ViewModel;
        Services.KeybindingService.Apply(this, ViewModel);
        PreviewKeyDown += (s, e) => Services.KeybindingService.HandleZenKeys(e, ViewModel);
        ViewModel.FullscreenRequested = ToggleFullscreen;
        UpdateStartupOptions(startupPath);

        // Collapse/expand sidebar and inspector columns when panel visibility changes
        Services.PanelService.Instance.PropertyChanged += (s, ea) =>
        {
            if (ea.PropertyName == nameof(Services.PanelService.SidePanelVisible))
            {
                sidebarColumn.Width = Services.PanelService.Instance.SidePanelVisible
                    ? new GridLength(320) : new GridLength(0);
            }
            else if (ea.PropertyName == nameof(Services.PanelService.InspectorVisible))
            {
                inspectorColumn.Width = Services.PanelService.Instance.InspectorVisible
                    ? new GridLength(280) : new GridLength(0);
            }
            else if (ea.PropertyName == nameof(Services.PanelService.ZenMode))
            {
                // Zen mode collapses both; exiting restores both
                if (Services.PanelService.Instance.ZenMode)
                {
                    sidebarColumn.Width = new GridLength(0);
                    inspectorColumn.Width = new GridLength(0);
                }
                // ExitZenMode sets SidePanelVisible/InspectorVisible=true which triggers the above
            }
        };
        // Apply saved panel state on startup
        if (!Services.PanelService.Instance.SidePanelVisible)
            sidebarColumn.Width = new GridLength(0);
        if (!Services.PanelService.Instance.InspectorVisible)
            inspectorColumn.Width = new GridLength(0);

        ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

        // wire up list selection from resources view
        resourcesView.ListSelectionChanged += ResourcesView_ListSelectionChanged;

        // wire up statusbar view model and service
        // initialize from main view model
        statusViewModel.StatusText = ViewModel.StatusText;
        statusViewModel.IsLoading = ViewModel.IsLoading;
        statusViewModel.ProjectName = System.IO.Path.GetFileName(ViewModel.CurrentPath) ?? ViewModel.CurrentPath ?? "Toucan Project";
        // show configured default language (app preference)
        statusViewModel.DefaultLanguage = ViewModel.AppOptions?.DefaultLanguage ?? "en-US";
        StatusBarService.Instance.Register(statusViewModel);
        RootStatusBar.DataContext = statusViewModel;

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
        _fileWatcher.FilesChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (System.Windows.MessageBox.Show("Files changed on disk. Reload?", "File Changed",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                ViewModel.RefreshCommand.Execute(null);
            }
        });
        ViewModel.PagedUpdates();
    }

    private void UpdateStartupOptions(string startupPath)
    {
        ViewModel.AppOptions = AppOptions.LoadFromDisk();
        ViewModel.CurrentPath = ViewModel.AppOptions.LastProjectPath ?? string.Empty;

        if (!string.IsNullOrEmpty(startupPath))
        {
            ViewModel.CurrentPath = startupPath;
            ViewModel.AppOptions.LastProjectPath = ViewModel.CurrentPath;
            ViewModel.AppOptions.ToDisk();
        }
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
        // Auto-open last project on startup
        if (!string.IsNullOrEmpty(ViewModel.CurrentPath) && System.IO.Directory.Exists(ViewModel.CurrentPath))
        {
            if (ViewModel.OpenRecentProjectCommand.CanExecute(ViewModel.CurrentPath))
            {
                await ViewModel.OpenRecentProjectCommand.ExecuteAsync(ViewModel.CurrentPath).ConfigureAwait(true);
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

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        PanelService.Instance.SaveLayout();
        StatusBarService.Instance.Unregister();
        _fileWatcher.Dispose();
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
