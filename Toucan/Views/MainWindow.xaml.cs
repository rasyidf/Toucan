using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Toucan;


public partial class MainWindow : FluentWindow
{
    internal MainWindowViewModel ViewModel { get; }
    private readonly Services.FileWatcherService _fileWatcher = new();

    internal MainWindow(string startupPath, MainWindowViewModel viewModel)
    {

        InitializeComponent();

        // ponytail: set icon via pack URI to avoid BAML TypeConverter error
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new System.Uri("pack://application:,,,/Assets/Images/WindowIcon.ico"));

        ViewModel = viewModel;
        DataContext = ViewModel;
        Services.KeybindingService.Apply(this, ViewModel);
        UpdateStartupOptions(startupPath);

        ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

        // wire up list selection from resources view
        resourcesView.ListSelectionChanged += ResourcesView_ListSelectionChanged;

        // wire up statusbar view model and service; prefer the DI-registered singleton instance
        var statusViewModel = App.Services != null
            ? App.Services.GetRequiredService<StatusBarViewModel>()
            : new StatusBarViewModel();
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
                statusViewModel.StatusText = ViewModel.StatusText;
            else if (ea.PropertyName == nameof(MainWindowViewModel.IsLoading))
                statusViewModel.IsLoading = ViewModel.IsLoading;
            else if (ea.PropertyName == nameof(MainWindowViewModel.CurrentPath))
            {
                statusViewModel.ProjectName = System.IO.Path.GetFileName(ViewModel.CurrentPath) ?? ViewModel.CurrentPath ?? "Toucan Project";
                _fileWatcher.Watch(ViewModel.CurrentPath);
            }
        };
        _fileWatcher.FilesChanged += () => Dispatcher.Invoke(() =>
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
        ViewModel.CurrentPath = ViewModel.AppOptions.LastProjectPath;

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
            return;

        ViewModel.SelectedNode.IsExpanded = true;

        string clickedNamespace = ViewModel.SelectedNode.Namespace;
        if (ViewModel.SelectedNode.HasItems)
            clickedNamespace += ".";

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
                    clickedNamespace += ".";

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
                await ViewModel.OpenRecentProjectCommand.ExecuteAsync(ViewModel.CurrentPath).ConfigureAwait(true);
        }

        // Populate recent projects for flyout menu
        ViewModel.RefreshRecentProjects();
        ViewModel.LoadFilterHistory();

        SystemThemeWatcher.Watch(this, WindowBackdropType.Tabbed, true);
    }

    // Search is now driven by the view-model SearchText property binding.


    private void NextPage(object sender, RoutedEventArgs e) => ViewModel.NextPageCommand.Execute(null);
    private void PreviousPage(object sender, RoutedEventArgs e) => ViewModel.PreviousPageCommand.Execute(null);
    private void FirstPage(object sender, RoutedEventArgs e) => ViewModel.FirstPageCommand.Execute(null);
    private void LastPage(object sender, RoutedEventArgs e) => ViewModel.LastPageCommand.Execute(null);

    private void UpdateLanguageValue(object sender, RoutedEventArgs e)
    {
        ViewModel.UpdateSummaryInfo();
        ViewModel.IsDirty = true;
    }

    private void ShowAll(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowAll(ViewModel.SearchText);
    }

    private void SearchFilterTextbox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var binding = ((System.Windows.Controls.TextBox)sender).GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
        }
    }
}
