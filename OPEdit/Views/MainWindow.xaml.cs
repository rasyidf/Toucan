using Ookii.Dialogs.Wpf;
using OPEdit.Core.Models;
using OPEdit.Core.Services;
using OPEditor.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls.Window;

namespace OPEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {

        //private List<LanguageSetting> allSettings;
        //private NsTreeItem selectedNode;
        //private readonly SummaryInfo summaryInfo = new();
        //private readonly PagingController<LanguageGroup> pagingController = new(30, new List<LanguageGroup>());
        //public List<NsTreeItem> currentTreeItems = new();

        //private AppOptions appOptions;
        //private string currentPath { get; set; }

        public ViewModels.MainWindowViewModel ViewModel
        {
            get;
        }

        public MainWindow(string startupPath, ViewModels.MainWindowViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            ViewModel.AppOptions = AppOptions.LoadFromDisk();

            if (!string.IsNullOrEmpty(startupPath))
            {

                ViewModel.CurrentPath = startupPath;
                ViewModel.AppOptions.DefaultPath = ViewModel.CurrentPath;
                ViewModel.AppOptions.ToDisk();
            }

            ViewModel.CurrentPath = ViewModel.AppOptions.DefaultPath;

            ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);


            RoutedCommand saveCommand = new();
            saveCommand.InputGestures.Add(new KeyGesture(Key.S, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(saveCommand, Save));

            RoutedCommand refreshCommand = new();
            refreshCommand.InputGestures.Add(new KeyGesture(Key.R, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(refreshCommand, Refresh));


            RoutedCommand deleteCommand = new();
            deleteCommand.InputGestures.Add(new KeyGesture(Key.Delete, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(deleteCommand, DeleteItem));

            RoutedCommand renameCommand = new();
            renameCommand.InputGestures.Add(new KeyGesture(Key.F2, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(renameCommand, RenameItem));

            RoutedCommand newCommand = new();
            newCommand.InputGestures.Add(new KeyGesture(Key.A, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(newCommand, NewItem));

            RoutedCommand newLanguageCommand = new();
            newLanguageCommand.InputGestures.Add(new KeyGesture(Key.L, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(newLanguageCommand, NewLanguage));

            RoutedCommand openFolderCommand = new();
            openFolderCommand.InputGestures.Add(new KeyGesture(Key.O, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(openFolderCommand, OpenFolder));

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
                itemMenu.IsEnabled = false;
                return;
            }
            ViewModel.SelectedNode.IsExpanded = true;
            itemMenu.IsEnabled = true;


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
            this.TreeNamespace.SelectedItemChanged += TreeNamespace_SelectedItemChanged;
            SearchFilterTextbox.TextChanged += SearchFilterTextbox_TextChanged;
            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentPath))
            {
                LoadFolder(ViewModel.CurrentPath);
            }

            Watcher.Watch(this, WindowBackdropType.Acrylic, true);
        }



        private void LoadFolder(string path)
        {
            ViewModel.AllSettings = new ProjectHelper().Load(path).ToList();
            AddMissingTranslations();
            RefreshTree();
            UpdateSummaryInfo(); 
        }

        private void RefreshTree(string selectNamespace = "")
        {

            IEnumerable<NsTreeItem> nodes = ViewModel.AllSettings.ForParse().ToNsTree();
            ViewModel.CurrentTreeItems.Clear();
            foreach (NsTreeItem node in nodes)
            {
                ViewModel.CurrentTreeItems.Add(node);
            }

            TreeNamespace.ItemsSource = null;
            TreeNamespace.ItemsSource = ViewModel.CurrentTreeItems;

            itemMenu.IsEnabled = false;

        }

        private void UpdateSummaryInfo()
        {
            ViewModel.SummaryInfo.Update(ViewModel.AllSettings);
            summaryControl.ItemsSource = null;
            summaryControl.ItemsSource = ViewModel.SummaryInfo.Details;
        }

        private void Search(string ns, bool alwaysPaging = false)
        {

            bool isPartial = false;
            IEnumerable<LanguageSetting> matchedSettings = ViewModel.AllSettings.ForParse();


            int settingCount = 0;
            if (ns.EndsWith("."))
            {
                List<LanguageSetting> settings = matchedSettings.Where(o => o.Namespace.StartsWith(ns)).ToList();

                if (!alwaysPaging && (settings.Count / 3 > ViewModel.AppOptions.TruncateResultsOver))
                {
                    isPartial = true;
                    matchedSettings = settings.Take(ViewModel.AppOptions.TruncateResultsOver);
                    settingCount = settings.Count;
                }
                else
                    matchedSettings = settings.ToList();
            }
            else
            {
                matchedSettings = matchedSettings.Where(o => o.Namespace == ns).ToList();
            }

            List<string> namespaces = matchedSettings.ToNamespaces().ToList();
            List<string> languages = ViewModel.AllSettings.ToLanguages().ToList();


            List<LanguageGroup> languageGroups = new();
            foreach (string n in namespaces)
            {
                LanguageGroup languageGroup = new(n, languages);
                languageGroup.LoadSettings(matchedSettings.Where(o => o.Namespace == n).ToList());
                languageGroups.Add(languageGroup);
            }

            ViewModel.PagingController.SwapData(languageGroups, isPartial);
            languageGroupContainer.ItemsSource = ViewModel.PagingController.PageData;
            pagingMessage.Text = ViewModel.PagingController.PageMessage;
            partialPagingButton.Visibility = isPartial ? Visibility.Visible : Visibility.Hidden;
            if (isPartial)
                partialPagingButton.Content = "Load " + settingCount / 3;

            ContentScroller.ScrollToTop();

            pagingButtons.Visibility = ViewModel.PagingController.HasPages ? Visibility.Visible : Visibility.Hidden;
        }

        private void ShowAll(object sender, RoutedEventArgs e)
        {
            Search(SearchFilterTextbox.Text, true);
        }


        private void SearchFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Search(SearchFilterTextbox.Text);
        }

        private void AddMissingTranslations()
        {
            List<string> namespaces = ViewModel.AllSettings.ToNamespaces().ToList();
            List<string> allLanguages = ViewModel.AllSettings.ToLanguages().ToList();

            foreach (string language in allLanguages)
            {
                IEnumerable<string> languageNamespaces = ViewModel.AllSettings.OnlyLanguage(language).ToNamespaces();
                ViewModel.AllSettings.AddRange(namespaces.Except(languageNamespaces).Select(o => new LanguageSetting() { Namespace = o, Value = string.Empty, Language = language }));
            }

        }

        private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
        {
            TextBox txtBox = (TextBox)sender;
            LanguageSetting setting = (LanguageSetting)txtBox.Tag;
            setting.Value = txtBox.Text;
            UpdateSummaryInfo();
        }
        private void NewFolder(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = ViewModel.CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                ViewModel.CurrentPath = dialog.SelectedPath;
                ViewModel.AppOptions.DefaultPath = ViewModel.CurrentPath;
                ProjectHelper.CreateLanguage(ViewModel.CurrentPath, "en-US");
                LoadFolder(ViewModel.CurrentPath);
            }
        }

        private void OpenFolder(object sender, RoutedEventArgs e)
        {

            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = ViewModel.CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                ViewModel.CurrentPath = dialog.SelectedPath;
                ViewModel.AppOptions.DefaultPath = ViewModel.CurrentPath;
                LoadFolder(ViewModel.CurrentPath);
            }
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            switch (ViewModel.AppOptions.SaveStyle)
            {
                case SaveStyles.Json:
                    ProjectHelper.SaveJson(ViewModel.CurrentPath, ViewModel.AllSettings.ToLanguageDictionary());
                    break;
                case SaveStyles.Namespaced:
                    ProjectHelper.SaveNsJson(ViewModel.CurrentPath, ViewModel.CurrentTreeItems.ToList(), ViewModel.AllSettings.ToLanguages().ToList());
                    break;
            }

        }
        private void SaveTo(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = ViewModel.CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                ViewModel.CurrentPath = dialog.SelectedPath;
                Save(null, null);
            }


        }

        private void Refresh(object sender, RoutedEventArgs e)
        {
            LoadFolder(ViewModel.CurrentPath);
        }
        private void NewItem(object sender, RoutedEventArgs e)
        {
            NsTreeItem node = (NsTreeItem)TreeNamespace.SelectedItem;
            string ns = node == null ? string.Empty : node.Namespace;

            Prompt dialog = new("New Translation", "Enter the translation name below.", ns) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (string.IsNullOrWhiteSpace(dialog.ResponseText))
                return;

            if (ViewModel.AllSettings.NoEmpty().Any(setting => setting.Namespace.Contains(dialog.ResponseText)))
            {
                MessageBox.Show("Duplicate name");
                return;
            }

            List<string> languages = ViewModel.AllSettings.ToLanguages().ToList();
            foreach (string language in languages)
            {
                string val = string.Empty;
                ViewModel.AllSettings.Add(new LanguageSetting() { Namespace = dialog.ResponseText, Value = val, Language = language });
            }
            RefreshTree(dialog.ResponseText);
            UpdateSummaryInfo();
        }
        private void NewLanguage(object sender, RoutedEventArgs e)
        {

            Prompt dialog = new("New Language", "Enter the translation language name below.") { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (string.IsNullOrWhiteSpace(dialog.ResponseText))
                return;

            if (ViewModel.AllSettings.Any(setting => setting.Language == dialog.ResponseText))
            {
                MessageBox.Show("Duplicate language");
                return;
            }

            LanguageSetting newSetting = new() { Namespace = "", Value = "", Language = dialog.ResponseText };

            ViewModel.AllSettings.Add(newSetting);
            AddMissingTranslations();
            UpdateSummaryInfo();
            RefreshTree();
            languageGroupContainer.ItemsSource = null;
        }
        private void RenameItem(object sender, RoutedEventArgs e)
        {
            NsTreeItem node = (NsTreeItem)TreeNamespace.SelectedItem;

            if (node == null)
                return;

            string ns = node.Namespace;
            string originalName = node.Name;

            Prompt dialog = new("Rename: " + originalName, "Enter the new name below.", originalName);
            if (dialog.ShowDialog() != true)
                return;

            if (string.IsNullOrWhiteSpace(dialog.ResponseText))
                return;

            if (dialog.ResponseText.Contains('.'))
                return;


            string newNs = ns[..ns.LastIndexOf(node.Name)] + dialog.ResponseText.Trim();

            ViewModel.AllSettings.ForParse().ToList().ForEach((item) =>
            {
                if (item.Namespace.StartsWith(ns))
                    item.Namespace = item.Namespace.Replace(ns, newNs);
            });

            RefreshTree(newNs);
        }
        private void DeleteItem(object sender, RoutedEventArgs e)
        {
            NsTreeItem node = (NsTreeItem)TreeNamespace.SelectedItem;

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

            ViewModel.AllSettings.RemoveAll(o => o?.Namespace?.StartsWith(ns) ?? false);

            RefreshTree();
        }
 
        private void ShowPreferences(object sender, RoutedEventArgs e)
        {
            Options optionsHwnd = new(ViewModel.AppOptions) { Owner = this };
            bool? saved = optionsHwnd.ShowDialog();
            if (saved.GetValueOrDefault())
            {
                ViewModel.AppOptions = optionsHwnd.Config;
            }
            ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);

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
            languageGroupContainer.ItemsSource = ViewModel.PagingController.PageData;
            pagingMessage.Text = ViewModel.PagingController.PageMessage;
            ContentScroller.ScrollToTop();
        }

    }
}
