using Google.Api.Gax;
using Google.Cloud.Translation.V2;
using Ookii.Dialogs.Wpf;
using OPEdit.Core.Models;
using OPEdit.Core.Services;
using OPEdit.Extensions;
using OPEdit.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls.Window;

namespace OPEdit;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private static HttpClient sharedClient = new()
    {
        BaseAddress = new Uri("https://libretranslate.com"),
    };

    public MainWindowViewModel ViewModel
    {
        get;
    }

    public MainWindow(string startupPath, MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        UpdateStartupOptions(startupPath);

        //RegisterCommands();

        ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

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

    private void RegisterCommands()
    {
        RoutedCommand deleteCommand = new();
        deleteCommand.InputGestures.Add(new KeyGesture(Key.Delete, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(deleteCommand, DeleteItem));

        RoutedCommand renameCommand = new();
        renameCommand.InputGestures.Add(new KeyGesture(Key.F2, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(renameCommand, RenameItem));

        RoutedCommand newCommand = new();
        newCommand.InputGestures.Add(new KeyGesture(Key.I, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(newCommand, NewItem));

        RoutedCommand newLanguageCommand = new();
        newLanguageCommand.InputGestures.Add(new KeyGesture(Key.L, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(newLanguageCommand, NewLanguage));

        RoutedCommand nextPageCommand = new();
        nextPageCommand.InputGestures.Add(new KeyGesture(Key.Right, ModifierKeys.Alt));
        CommandBindings.Add(new CommandBinding(nextPageCommand, NextPage));

        RoutedCommand previousPageCommand = new();
        previousPageCommand.InputGestures.Add(new KeyGesture(Key.Left, ModifierKeys.Alt));
        CommandBindings.Add(new CommandBinding(previousPageCommand, PreviousPage));

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
            clickedNamespace += ".";

        if (string.IsNullOrWhiteSpace(clickedNamespace))
        {
            return;
        }

        SearchFilterTextbox.Text = clickedNamespace;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenRecent();

        SearchFilterTextbox.TextChanged += SearchFilterTextbox_TextChanged;

        Watcher.Watch(this, WindowBackdropType.Tabbed, true);
    }


    private void SearchFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
    {
       
        ViewModel.Search(SearchFilterTextbox.Text, false);
        
    }


    private void NewItem(object sender, RoutedEventArgs e)
    {
        NsTreeItem node = (NsTreeItem)resourcesView.TreeNamespace.SelectedItem;
        string ns = node == null ? string.Empty : node.Namespace;

        PromptDialog dialog = new("New Translation", "Please enter an ID for the translatiom\r\nUse '.' to create hierarchical IDs, e.g. dashboard.title.label.", ns) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (string.IsNullOrWhiteSpace(dialog.ResponseText))
            return;

        if (ViewModel.AllTranslation.NoEmpty().Any(setting => setting.Namespace.Contains(dialog.ResponseText)))
        {
            MessageBox.Show("Duplicate name");
            return;
        }

        List<string> languages = ViewModel.AllTranslation.ToLanguages().ToList();
        foreach (string language in languages)
        {
            string val = string.Empty;
            ViewModel.AllTranslation.Add(new TranslationItem() { Namespace = dialog.ResponseText, Value = val, Language = language });
        }
        ViewModel.RefreshTree(dialog.ResponseText);
        ViewModel.UpdateSummaryInfo();
    }
    private void NewLanguage(object sender, RoutedEventArgs e)
    {

        LanguagePrompt dialog = new("New Language", "Enter the translation language name below.", ViewModel.AllTranslation) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (string.IsNullOrWhiteSpace(dialog.ResponseText))
            return;

        if (ViewModel.AllTranslation.Any(setting => setting.Language == dialog.ResponseText))
        {
            MessageBox.Show("Duplicate language");
            return;
        }

        TranslationItem newSetting = new() { Namespace = "", Value = "", Language = dialog.ResponseText };

        ViewModel.AllTranslation.Add(newSetting);
        ViewModel.AddMissingTranslations();
        ViewModel.UpdateSummaryInfo();
        ViewModel.RefreshTree();
        translationDetailsView.languageGroupContainer.ItemsSource = null;
    }
    private void RenameItem(object sender, RoutedEventArgs e)
    {
        NsTreeItem node = (NsTreeItem)resourcesView.TreeNamespace.SelectedItem;

        if (node == null)
            return;

        string ns = node.Namespace;
        string originalName = node.Name;

        PromptDialog dialog = new("Rename: " + originalName, "Enter the new name below.", originalName);
        if (dialog.ShowDialog() != true)
            return;

        if (string.IsNullOrWhiteSpace(dialog.ResponseText))
            return;

        if (dialog.ResponseText.Contains('.', StringComparison.InvariantCulture))
            return;


        string newNs = ns[..ns.LastIndexOf(node.Name, StringComparison.InvariantCulture)] + dialog.ResponseText.Trim();

        ViewModel.AllTranslation.ForParse().ToList().ForEach((item) =>
        {
            if (item.Namespace.StartsWith(ns, StringComparison.InvariantCulture))
                item.Namespace = item.Namespace.Replace(ns, newNs, StringComparison.InvariantCulture);
        });

        ViewModel.RefreshTree(newNs);
    }
    private void DeleteItem(object sender, RoutedEventArgs e)
    {
        NsTreeItem node = (NsTreeItem)resourcesView.TreeNamespace.SelectedItem;

        if (node == null)
            return;

        string ns = node.Namespace;

        if (string.IsNullOrWhiteSpace(ns))
            return;

        if (node.Parent == null)
        {
            ViewModel.CurrentTreeItems.Remove(node);
        }
        else
        {
            List<NsTreeItem> nodes = node.Parent.Items as List<NsTreeItem>;
            nodes?.Remove(node);
            //   node.Parent.IsSelected = true;
        }

        ViewModel.AllTranslation.RemoveAll(o => o?.Namespace?.StartsWith(ns) ?? false);

        ViewModel.RefreshTree();
    }

    private void NextPage(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PagingController == null || !ViewModel.PagingController.HasNextPage)
            return;
        ViewModel.PagingController.NextPage();
        PagedUpdates();
    }
    private void PreviousPage(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PagingController == null || !ViewModel.PagingController.HasPreviousPage)
            return;
        ViewModel.PagingController.PreviousPage();
        PagedUpdates();

    }
    private void FirstPage(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PagingController == null || !ViewModel.PagingController.HasPreviousPage)
            return;
        ViewModel.PagingController.MoveFirst();
        PagedUpdates();

    }
    private void LastPage(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PagingController == null || !ViewModel.PagingController.HasNextPage)
            return;
        ViewModel.PagingController.LastPage();
        PagedUpdates();

    }

    private void PagedUpdates()
    {
        translationDetailsView.languageGroupContainer.ItemsSource = ViewModel.PagingController.PageData;
        translationDetailsView.pagingMessage.Text = ViewModel.PagingController.PageMessage;
        translationDetailsView.ContentScroller.ScrollToTop();
    }


    static async Task<string> PostAsync(HttpClient httpClient)
    {
        using StringContent jsonContent = new(
            JsonSerializer.Serialize(new
            {
                q = "Hello!",
                source = "en",
                target = "es"
            }),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await httpClient.PostAsync("translate", jsonContent);

        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync();
        return jsonResponse;
        // Expected output:
        //   POST https://jsonplaceholder.typicode.com/todos HTTP/1.1
        //   {
        //     "userId": 77,
        //     "id": 201,
        //     "title": "write code sample",
        //     "completed": false
        //   }
    }

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
