using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPEdit.Core.Models;
using OPEdit.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OPEdit.Views;
using Google.Cloud.Translation.V2;
using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.Windows.Input;
using Ookii.Dialogs.Wpf;
using OPEdit.Core.Extensions;

namespace OPEdit.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private List<TranslationItem> allTranslation;

    [ObservableProperty]
    private NsTreeItem selectedNode;

    [ObservableProperty]
    private SummaryInfoViewModel summaryInfo = new();

    [ObservableProperty]
    private PagingController<LanguageGroup> pagingController = new(30, new List<LanguageGroup>());

    [ObservableProperty]
    private ObservableCollection<NsTreeItem> currentTreeItems = new();


    [ObservableProperty]
    private AppOptions appOptions;

    [ObservableProperty]
    private string currentPath;

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string searchText;

    [ObservableProperty]
    private string statusText;
    public IEnumerable<LanguageGroup> PageData { get; private set; }
    public string PageMessage { get; private set; }



    #region Help Commands

    [RelayCommand]
    void HelpHomepage()
    {
        // Redirect to Rasyid.dev
        OpenUrl("https://rasyid.dev");
    }

    [RelayCommand]
    void HelpAbout()
    {
        AboutDialog ad = new(App.Current.MainWindow);
        ad.ShowDialog();
    }

    #endregion

    #region Pagination

    [RelayCommand]
    private void NextPage()
    {
        PagingController.NextPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        PagingController.PreviousPage();
        PagedUpdates();
    }

    [RelayCommand]
    private void FirstPage()
    {
        PagingController.MoveFirst();
        PagedUpdates();
    }

    [RelayCommand]
    private void LastPage()
    {
        PagingController.LastPage();
        PagedUpdates();
    }

    internal void PagedUpdates()
    {
        PageData = PagingController.PageData;
        PageMessage = PagingController.PageMessage;
    }
    #endregion

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(url);
        }
        catch
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&", StringComparison.InvariantCulture);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                throw;
            }
        }
    }
    internal void RefreshTree(string selectNamespace = "")
    {

        IEnumerable<NsTreeItem> nodes = AllTranslation.ForParse().ToNsTree();
        CurrentTreeItems.Clear();
        foreach (NsTreeItem node in nodes)
        {
            CurrentTreeItems.Add(node);
        }

    }

    [RelayCommand]
    private void Exit()
    {
        Application.Current.Shutdown();
    }
    internal void UpdateSummaryInfo()
    {
        SummaryInfo.Update(AllTranslation);
    }

    internal void Search(string ns, bool alwaysPaging = false)
    {

        bool isPartial = false;
        List<TranslationItem> matchedTranslations = AllTranslation.ToList();
        List<TranslationItem> translationItems;


        int TranslationCount = 0;
        if (string.IsNullOrWhiteSpace(ns))
        {
            translationItems = matchedTranslations.ToList();
        }
        else if (!ns.EndsWith(".", StringComparison.InvariantCultureIgnoreCase))
        {
            translationItems = matchedTranslations.Where(o => o.Namespace == ns).ToList();
        }
        else
        {
            List<TranslationItem> translations = matchedTranslations.Where(o => o.Namespace.StartsWith(ns, StringComparison.InvariantCulture)).ToList();

            if (!alwaysPaging && (translations.Count / 3 > AppOptions.TruncateResultsOver))
            {
                isPartial = true;
                translationItems = translations.Take(AppOptions.TruncateResultsOver).ToList();
                TranslationCount = translations.Count;
            }
            else
                translationItems = translations.ToList();
        }

        List<string> namespaces = translationItems.ToNamespaces().ToList();
        List<string> languages = AllTranslation.ToLanguages().ToList();


        List<LanguageGroup> languageGroups = new();
        foreach (string n in namespaces)
        {
            LanguageGroup languageGroup = new(n, languages);
            languageGroup.LoadTranslations(matchedTranslations.Where(o => o.Namespace == n).ToList());
            languageGroups.Add(languageGroup);
        }

        PagingController.SwapData(languageGroups);
    }

    internal void ShowAll(string Path)
    {
        Search(Path, true);
    }
    internal void AddMissingTranslations()
    {
        List<string> namespaces = AllTranslation.ToNamespaces().ToList();
        List<string> allLanguages = AllTranslation.ToLanguages().ToList();

        foreach (string language in allLanguages)
        {
            IEnumerable<string> languageNamespaces = AllTranslation.OnlyLanguage(language).ToNamespaces();
            AllTranslation.AddRange(namespaces.Except(languageNamespaces).Select(o => new TranslationItem() { Namespace = o, Value = string.Empty, Language = language }));
        }

    }

    [RelayCommand]
    private void ShowAll()
    {
        Search("", true);
    }

    #region File Menu


    [RelayCommand]
    private void NewFolder()
    {
        VistaFolderBrowserDialog dialog = new()
        {
            SelectedPath = CurrentPath
        };
        bool? selected = dialog.ShowDialog(App.Current.MainWindow);
        if (selected.GetValueOrDefault())
        {
            CurrentPath = dialog.SelectedPath;
            AppOptions.DefaultPath = CurrentPath;
            ProjectHelper.CreateLanguage(CurrentPath, "en-US");
            LoadFolder(CurrentPath);
        }
    }
    [RelayCommand]
    private void OpenFolder()
    {

        VistaFolderBrowserDialog dialog = new()
        {
            SelectedPath = CurrentPath
        };
        bool? selected = dialog.ShowDialog(App.Current.MainWindow);
        if (selected.GetValueOrDefault())
        {
            CurrentPath = dialog.SelectedPath;
            AppOptions.DefaultPath = CurrentPath;
            LoadFolder(CurrentPath);
        }
    }
    private void LoadFolder(string path)
    {
        AllTranslation = ProjectHelper.Load(path);
        AddMissingTranslations();

        RefreshTree();
        UpdateSummaryInfo();
        IsDirty = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        IsDirty = false;
        switch (AppOptions.SaveStyle)
        {
            case SaveStyles.Json:
                ProjectHelper.SaveJson(CurrentPath, AllTranslation.ToLanguageDictionary());
                break;
            case SaveStyles.Namespaced:
                ProjectHelper.SaveNsJson(CurrentPath, CurrentTreeItems.ToList(), AllTranslation.ToLanguages().ToList());
                break;
        }
    }

    private bool CanSave()
    {
        return isDirty;
    }

    [RelayCommand]
    private void SaveTo()
    {
        VistaFolderBrowserDialog dialog = new()
        {
            SelectedPath = CurrentPath
        };
        bool? selected = dialog.ShowDialog(App.Current.MainWindow);
        if (selected.GetValueOrDefault())
        {
            CurrentPath = dialog.SelectedPath;
            Save();
        }

    }
    [RelayCommand]
    private void CloseProject()
    {
        AllTranslation = new List<TranslationItem>();
        CurrentPath = "";

        RefreshTree();
        UpdateSummaryInfo();
    }
    [RelayCommand]
    private void Refresh()
    {
        LoadFolder(CurrentPath);
    }
    [RelayCommand]
    internal void OpenRecent()
    {

        LoadFolder(AppOptions.DefaultPath);

    }

    [RelayCommand]
    private void ShowPreferences()
    {
        OptionDialog optionsHwnd = new(AppOptions) { Owner = App.Current.MainWindow };
        bool? saved = optionsHwnd.ShowDialog();
        if (saved.GetValueOrDefault())
        {
            AppOptions = optionsHwnd.Config;
        }
        PagingController.UpdatePageSize(AppOptions.PageSize);

    }
    #endregion

}

