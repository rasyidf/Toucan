using System.Windows;
using System.Windows.Controls;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.ViewModels;
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
            SearchFilterTextbox.Text = clickedNamespace;
        }
    }

    private void ResourcesView_ListSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is global::System.Windows.Controls.ListView lv && lv.SelectedItem is NsFlatItem flat)
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
                    SearchFilterTextbox.Text = clickedNamespace;
                }
            }
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenRecent();
        SearchFilterTextbox.TextChanged += SearchFilterTextbox_TextChanged;
        SystemThemeWatcher.Watch(this, WindowBackdropType.Tabbed, true);
    }

    private void SearchFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search(SearchFilterTextbox.Text, false);
    }


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
        ViewModel.ShowAll(SearchFilterTextbox.Text);
    }
}
