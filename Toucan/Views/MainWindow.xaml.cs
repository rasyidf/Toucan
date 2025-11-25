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
     

    internal MainWindow(string startupPath, MainWindowViewModel viewModel)
    {

        InitializeComponent();

        ViewModel = viewModel;
        DataContext = ViewModel;
        UpdateStartupOptions(startupPath);

        ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

        // wire up list selection from resources view
        resourcesView.ListSelectionChanged += ResourcesView_ListSelectionChanged;

        // wire up statusbar view model and service
        var statusViewModel = new StatusBarViewModel();
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
            }
        };
            ViewModel.PagedUpdates();
    }

    private void UpdateStartupOptions(string startupPath)
    {
        ViewModel.AppOptions = AppOptions.LoadFromDisk();
        ViewModel.CurrentPath = ViewModel.AppOptions.DefaultPath;

        if (!string.IsNullOrEmpty(startupPath))
        {
            ViewModel.CurrentPath = startupPath;
            ViewModel.AppOptions.DefaultPath = ViewModel.CurrentPath;
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
        await ViewModel.OpenRecent().ConfigureAwait(true);
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
}
