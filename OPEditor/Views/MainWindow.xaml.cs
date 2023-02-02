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

        private List<LanguageSetting> allSettings;
        private NsTreeItem selectedNode;
        private readonly SummaryInfo summaryInfo = new();
        private readonly PagingController<LanguageGroup> pagingController = new(30, new List<LanguageGroup>());
        public List<NsTreeItem> CurrentTreeItems = new();

        private AppOptions appOptions;
        private string CurrentPath { get; set; }
        public MainWindow(string startupPath)
        {
            InitializeComponent();


            CurrentPath = startupPath;

            appOptions = AppOptions.FromDisk();

            if (!string.IsNullOrEmpty(startupPath))
            {
                appOptions.DefaultPath = CurrentPath;
                appOptions.ToDisk();
            }

            CurrentPath = appOptions.DefaultPath;
            pagingController.UpdatePageSize(appOptions.PageSize);


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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.TreeNamespace.SelectedItemChanged += TreeNamespace_SelectedItemChanged;
            SearchFilterTextbox.TextChanged += SearchFilterTextbox_TextChanged;
            if (!string.IsNullOrWhiteSpace(CurrentPath))
            {
                LoadFolder(CurrentPath);
            }

            Watcher.Watch(
                   this,                    // Window class
                   WindowBackdropType.Acrylic, // Background type
                   true                     // Whether to change accents automatically
               );
        }



        private void LoadFolder(string path)
        {
            allSettings = new ProjectHelper().Load(path).ToList();
            AddMissingTranslations();
            RefreshTree();
            UpdateSummaryInfo();


        }

        private void RefreshTree(string selectNamespace = "")
        {

            IEnumerable<NsTreeItem> nodes = allSettings.ForParse().ToNsTree();
            CurrentTreeItems.Clear();
            foreach (NsTreeItem node in nodes)
            {
                CurrentTreeItems.Add(node);
            }

            TreeNamespace.ItemsSource = null;
            TreeNamespace.ItemsSource = CurrentTreeItems;

            itemMenu.IsEnabled = false;

        }

        private void UpdateSummaryInfo()
        {
            summaryInfo.Update(allSettings);
            summaryControl.ItemsSource = null;
            summaryControl.ItemsSource = summaryInfo.Details;
        }

        private void TreeNamespace_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            selectedNode = (NsTreeItem)e.NewValue;
            if (selectedNode == null)
            {
                itemMenu.IsEnabled = false;
                return;
            }
            selectedNode.IsExpanded = true;
            itemMenu.IsEnabled = true;


            string clickedNamespace = selectedNode.Namespace;
            if (selectedNode.HasItems)
                clickedNamespace += ".";

            if (string.IsNullOrWhiteSpace(clickedNamespace))
            {
                return;
            }

            SearchFilterTextbox.Text = clickedNamespace;
        }

        private void Search(string ns, bool alwaysPaging = false)
        {

            bool isPartial = false;
            IEnumerable<LanguageSetting> matchedSettings = allSettings.ForParse();


            int settingCount = 0;
            if (ns.EndsWith("."))
            {
                List<LanguageSetting> settings = matchedSettings.Where(o => o.Namespace.StartsWith(ns)).ToList();

                if (!alwaysPaging && (settings.Count / 3 > appOptions.TruncateResultsOver))
                {
                    isPartial = true;
                    matchedSettings = settings.Take(appOptions.TruncateResultsOver);
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
            List<string> languages = allSettings.ToLanguages().ToList();


            List<LanguageGroup> languageGroups = new();
            foreach (string n in namespaces)
            {
                LanguageGroup languageGroup = new(n, languages);
                languageGroup.LoadSettings(matchedSettings.Where(o => o.Namespace == n).ToList());
                languageGroups.Add(languageGroup);
            }

            pagingController.SwapData(languageGroups, isPartial);
            languageGroupContainer.ItemsSource = pagingController.PageData;
            pagingMessage.Text = pagingController.PageMessage;
            partialPagingButton.Visibility = isPartial ? Visibility.Visible : Visibility.Hidden;
            if (isPartial)
                partialPagingButton.Content = "Load " + settingCount / 3;

            ContentScroller.ScrollToTop();

            pagingButtons.Visibility = pagingController.HasPages ? Visibility.Visible : Visibility.Hidden;
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
            List<string> namespaces = allSettings.ToNamespaces().ToList();
            List<string> allLanguages = allSettings.ToLanguages().ToList();

            foreach (string language in allLanguages)
            {
                IEnumerable<string> languageNamespaces = allSettings.OnlyLanguage(language).ToNamespaces();
                allSettings.AddRange(namespaces.Except(languageNamespaces).Select(o => new LanguageSetting() { Namespace = o, Value = string.Empty, Language = language }));
            }

        }




        private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
        {
            TextBox txtBox = (TextBox)sender;
            LanguageSetting setting = (LanguageSetting)txtBox.Tag;
            setting.Value = txtBox.Text;
            UpdateSummaryInfo();
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            switch (appOptions.SaveStyle)
            {
                case SaveStyles.Json:
                    ProjectHelper.SaveJson(CurrentPath, allSettings.ToLanguageDictionary());
                    break;
                case SaveStyles.Namespaced:
                    ProjectHelper.SaveNsJson(CurrentPath, CurrentTreeItems.ToList(), allSettings.ToLanguages().ToList());
                    break;
            }

        }
        private void SaveTo(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                CurrentPath = dialog.SelectedPath;
                Save(null, null);
            }


        }


        private void Refresh(object sender, RoutedEventArgs e)
        {
            LoadFolder(CurrentPath);
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

            if (allSettings.NoEmpty().Any(setting => setting.Namespace.Contains(dialog.ResponseText)))
            {
                MessageBox.Show("Duplicate name");
                return;
            }

            List<string> languages = allSettings.ToLanguages().ToList();
            foreach (string language in languages)
            {
                string val = string.Empty;
                allSettings.Add(new LanguageSetting() { Namespace = dialog.ResponseText, Value = val, Language = language });
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

            if (allSettings.Any(setting => setting.Language == dialog.ResponseText))
            {
                MessageBox.Show("Duplicate language");
                return;
            }

            LanguageSetting newSetting = new() { Namespace = "", Value = "", Language = dialog.ResponseText };

            allSettings.Add(newSetting);
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

            allSettings.ForParse().ToList().ForEach((item) =>
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
                CurrentTreeItems.Remove(node);
            }
            else
            {
                List<NsTreeItem> nodes = node.Parent.Items as List<NsTreeItem>;
                nodes.Remove(node);
                //   node.Parent.IsSelected = true;
            }

            allSettings.RemoveAll(o => o?.Namespace?.StartsWith(ns) ?? false);

            RefreshTree();
        }
        private void NewFolder(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                CurrentPath = dialog.SelectedPath;
                appOptions.DefaultPath = CurrentPath;
                ProjectHelper.CreateLanguage(CurrentPath, "en-US");
                LoadFolder(CurrentPath);
            }
        }

        private void OpenFolder(object sender, RoutedEventArgs e)
        {

            VistaFolderBrowserDialog dialog = new()
            {
                SelectedPath = CurrentPath
            };
            bool? selected = dialog.ShowDialog(this);
            if (selected.GetValueOrDefault())
            {
                CurrentPath = dialog.SelectedPath;
                appOptions.DefaultPath = CurrentPath;
                LoadFolder(CurrentPath);
            }
        }

        private void ShowPreferences(object sender, RoutedEventArgs e)
        {
            Options optionsHwnd = new(appOptions);
            bool? saved = optionsHwnd.ShowDialog();
            if (saved.GetValueOrDefault())
            {
                appOptions = optionsHwnd.Config;
            }
            pagingController.UpdatePageSize(appOptions.PageSize);

        }

        private void NextPage(object sender, RoutedEventArgs e)
        {
            if (pagingController == null || !pagingController.HasNextPage)
                return;
            pagingController.NextPage();
            PagedUpdates();
        }
        private void PreviousPage(object sender, RoutedEventArgs e)
        {
            if (pagingController == null || !pagingController.HasPreviousPage)
                return;
            pagingController.PreviousPage();
            PagedUpdates();

        }
        private void FirstPage(object sender, RoutedEventArgs e)
        {
            if (pagingController == null || !pagingController.HasPreviousPage)
                return;
            pagingController.MoveFirst();
            PagedUpdates();

        }
        private void LastPage(object sender, RoutedEventArgs e)
        {
            if (pagingController == null || !pagingController.HasNextPage)
                return;
            pagingController.LastPage();
            PagedUpdates();

        }

        private void PagedUpdates()
        {
            languageGroupContainer.ItemsSource = pagingController.PageData;
            pagingMessage.Text = pagingController.PageMessage;
            ContentScroller.ScrollToTop();
        }

    }
}
