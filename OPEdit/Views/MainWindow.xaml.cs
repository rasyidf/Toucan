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

namespace OPEdit
{
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

            ViewModel.AppOptions = AppOptions.LoadFromDisk();
            ViewModel.CurrentPath = ViewModel.AppOptions.DefaultPath;

            if (!string.IsNullOrEmpty(startupPath))
            {

                ViewModel.CurrentPath = startupPath;
                ViewModel.AppOptions.DefaultPath = ViewModel.CurrentPath;
                ViewModel.AppOptions.ToDisk();
            }



            ViewModel.PagingController.UpdatePageSize(ViewModel.AppOptions.PageSize);


            RegisterCommands();

        }
        private void RegisterCommands()
        {
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

            //RoutedCommand newCommand = new();
            //newCommand.InputGestures.Add(new KeyGesture(Key.I, ModifierKeys.Control));
            //CommandBindings.Add(new CommandBinding(newCommand, NewItem));

            //RoutedCommand newLanguageCommand = new();
            //newLanguageCommand.InputGestures.Add(new KeyGesture(Key.L, ModifierKeys.Control));
            //CommandBindings.Add(new CommandBinding(newLanguageCommand, NewLanguage));

            //RoutedCommand openFolderCommand = new();
            //openFolderCommand.InputGestures.Add(new KeyGesture(Key.O, ModifierKeys.Control));
            //CommandBindings.Add(new CommandBinding(openFolderCommand, OpenFolder));

            //RoutedCommand nextPageCommand = new();
            //nextPageCommand.InputGestures.Add(new KeyGesture(Key.Right, ModifierKeys.Alt));
            //CommandBindings.Add(new CommandBinding(nextPageCommand, NextPage));

            //RoutedCommand previousPageCommand = new();
            //previousPageCommand.InputGestures.Add(new KeyGesture(Key.Left, ModifierKeys.Alt));
            //CommandBindings.Add(new CommandBinding(previousPageCommand, PreviousPage));

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
            OpenRecent(sender, e);

            SearchFilterTextbox.TextChanged += SearchFilterTextbox_TextChanged;

            Watcher.Watch(this, WindowBackdropType.Acrylic, true);
        }


        private void SearchFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Search(SearchFilterTextbox.Text);
        }


        private void LoadFolder(string path)
        {
            ViewModel.AllTranslation = ProjectHelper.Load(path);
            AddMissingTranslations();

            RefreshTree();
            UpdateSummaryInfo();
        }

        private void RefreshTree(string selectNamespace = "")
        {

            IEnumerable<NsTreeItem> nodes = ViewModel.AllTranslation.ForParse().ToNsTree();
            ViewModel.CurrentTreeItems.Clear();
            foreach (NsTreeItem node in nodes)
            {
                ViewModel.CurrentTreeItems.Add(node);
            }

            resourcesView.TreeNamespace.ItemsSource = null;
            resourcesView.TreeNamespace.ItemsSource = ViewModel.CurrentTreeItems;


        }

        private void UpdateSummaryInfo()
        {
            ViewModel.SummaryInfo.Update(ViewModel.AllTranslation);
            languagesView.summaryControl.ItemsSource = null;
            languagesView.summaryControl.ItemsSource = ViewModel.SummaryInfo.Details;
        }

        private void Search(string ns, bool alwaysPaging = false)
        {

            bool isPartial = false;
            IEnumerable<TranslationItem> matchedSettings = ViewModel.AllTranslation.ForParse();


            int settingCount = 0;
            if (!ns.EndsWith(".", StringComparison.InvariantCultureIgnoreCase))
            {
                matchedSettings = matchedSettings.Where(o => o.Namespace == ns).ToList();
            }
            else
            {
                List<TranslationItem> settings = matchedSettings.Where(o => o.Namespace.StartsWith(ns)).ToList();

                if (!alwaysPaging && (settings.Count / 3 > ViewModel.AppOptions.TruncateResultsOver))
                {
                    isPartial = true;
                    matchedSettings = settings.Take(ViewModel.AppOptions.TruncateResultsOver);
                    settingCount = settings.Count;
                }
                else
                    matchedSettings = settings.ToList();
            }

            List<string> namespaces = matchedSettings.ToNamespaces().ToList();
            List<string> languages = ViewModel.AllTranslation.ToLanguages().ToList();


            List<LanguageGroup> languageGroups = new();
            foreach (string n in namespaces)
            {
                LanguageGroup languageGroup = new(n, languages);
                languageGroup.LoadSettings(matchedSettings.Where(o => o.Namespace == n).ToList());
                languageGroups.Add(languageGroup);
            }

            ViewModel.PagingController.SwapData(languageGroups, isPartial);
            translationDetailsView.languageGroupContainer.ItemsSource = ViewModel.PagingController.PageData;
            translationDetailsView.pagingMessage.Text = ViewModel.PagingController.PageMessage;
            translationDetailsView.partialPagingButton.Visibility = isPartial ? Visibility.Visible : Visibility.Hidden;
            if (isPartial)
                translationDetailsView.partialPagingButton.Content = "LoadAsync " + settingCount / 3;

            translationDetailsView.ContentScroller.ScrollToTop();

            translationDetailsView.pagingButtons.Visibility = ViewModel.PagingController.HasPages ? Visibility.Visible : Visibility.Hidden;
        }

        private void ShowAll(object sender, RoutedEventArgs e)
        {
            Search(SearchFilterTextbox.Text, true);
        }
        private void AddMissingTranslations()
        {
            List<string> namespaces = ViewModel.AllTranslation.ToNamespaces().ToList();
            List<string> allLanguages = ViewModel.AllTranslation.ToLanguages().ToList();

            foreach (string language in allLanguages)
            {
                IEnumerable<string> languageNamespaces = ViewModel.AllTranslation.OnlyLanguage(language).ToNamespaces();
                ViewModel.AllTranslation.AddRange(namespaces.Except(languageNamespaces).Select(o => new TranslationItem() { Namespace = o, Value = string.Empty, Language = language }));
            }

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
                    ProjectHelper.SaveJson(ViewModel.CurrentPath, ViewModel.AllTranslation.ToLanguageDictionary());
                    break;
                case SaveStyles.Namespaced:
                    ProjectHelper.SaveNsJson(ViewModel.CurrentPath, ViewModel.CurrentTreeItems.ToList(), ViewModel.AllTranslation.ToLanguages().ToList());
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
            NsTreeItem node = (NsTreeItem)resourcesView.TreeNamespace.SelectedItem;
            string ns = node == null ? string.Empty : node.Namespace;

            Prompt dialog = new("New Translation", "Please enter an ID for the translatiom\r\nUse '.' to create hierarchical IDs, e.g. dashboard.title.label.", ns) { Owner = this };
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
            RefreshTree(dialog.ResponseText);
            UpdateSummaryInfo();
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
            AddMissingTranslations();
            UpdateSummaryInfo();
            RefreshTree();
            translationDetailsView.languageGroupContainer.ItemsSource = null;
        }
        private void RenameItem(object sender, RoutedEventArgs e)
        {
            NsTreeItem node = (NsTreeItem)resourcesView.TreeNamespace.SelectedItem;

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

            ViewModel.AllTranslation.ForParse().ToList().ForEach((item) =>
            {
                if (item.Namespace.StartsWith(ns))
                    item.Namespace = item.Namespace.Replace(ns, newNs);
            });

            RefreshTree(newNs);
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

            using HttpResponseMessage response = await httpClient.PostAsync(
                "translate",
                jsonContent);

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

        async Task<String> Translate(string text, string from, string to)
        {
            var a = await PostAsync(sharedClient);
            MessageBox.Show(a);
            return "";
        }
        void PreTranslate(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SummaryInfo != null)
            {
                var Details = ViewModel.SummaryInfo.Details;
                var AllSet = ViewModel.AllTranslation;
                // Scan the completed data
                var Aset = AllSet.FindAll(x => x.Language == Details[0].Language);
                var Bset = AllSet.FindAll(x => x.Language == Details[1].Language);
                var isAfull = Details[0].Missing == 0;
                var isBfull = Details[1].Missing == 0;

                // Get Only 
                if (!isAfull)
                {
                    var AAset = Aset.FindAll(x => string.IsNullOrEmpty(x.Value));
                    // find corresponding Bset by AASet
                    var ABset = AAset.Select(x => Bset.Find(v => v.Namespace == x.Namespace));

                    IEnumerable<string> ACSet = ABset.Select(x => x?.Value);

                    TranslationClient client = TranslationClient.Create();
                    var ADSet = client.TranslateText(ACSet, Details[0].Language, Details[1].Language);

                    MessageBox.Show("Hehe", "Hehe");
                }

                if (!isBfull)
                {
                    var BAset = Bset.FindAll(x => string.IsNullOrEmpty(x.Value));
                    // find corresponding Bset by AASet
                    var BBset = BAset.Select(x => Bset.Find(v => v.Namespace == x.Namespace));

                    var BCSet = BBset.Select(x => x?.Value);

                    TranslationClient client = TranslationClient.Create();
                    var BDSet = client.TranslateText(BCSet, Details[0].Language, Details[1].Language);

                    MessageBox.Show("Hehe", "Hehe");

                }
            }

        }

        private void UpdateLanguageValue(object sender, RoutedEventArgs e)
        {
            UpdateSummaryInfo();
        }

        private void CloseProject(object sender, RoutedEventArgs e)
        {
            ViewModel.AllTranslation = new List<TranslationItem>();
            ViewModel.CurrentPath = "";

            RefreshTree();
            UpdateSummaryInfo();
        }

        private void OpenRecent(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.CurrentPath))
            {
                LoadFolder(ViewModel.CurrentPath);
            }
        }
    }
}
