using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Toucan.Core;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Services;
using Toucan.Views;

namespace Toucan.ViewModels;

internal partial class MainWindowViewModel : ObservableObject
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

    [ObservableProperty]
    private bool isTreeView = true;

    [ObservableProperty]
    private bool showStartScreen = true;

    // New properties for languages panel visibility and advanced options
    [ObservableProperty]
    private bool languagesVisible = true;

    [ObservableProperty]
    private bool showAdvancedOptions = false;


    public IEnumerable<LanguageGroup> PageData { get; private set; }
    public string PageMessage { get; private set; }

    private readonly IRecentProjectService _recentFileService;
    private readonly IDialogService _dialogService;
    private readonly IMessageService _messageService;
    private readonly IPreferenceService _preferenceService;
    private readonly IBulkActionService _bulkActionService;
    private readonly IProjectService _projectService;


    public MainWindowViewModel(
     IRecentProjectService recentFileService,
     IDialogService dialogService,
     IMessageService messageService,
     IPreferenceService preferenceService,
    IBulkActionService bulkActionService = null,
    IProjectService projectService = null)
    {
        _recentFileService = recentFileService;
        _dialogService = dialogService;
        _messageService = messageService;
        _preferenceService = preferenceService;
        _bulkActionService = bulkActionService;
        _projectService = projectService;

        AppOptions = _preferenceService.Load();
    }


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

    [RelayCommand]
    private void ToggleViewMode()
    {
        IsTreeView = !IsTreeView;
    }

    // New commands for UI control and bulk actions
    [RelayCommand]
    private void ToggleLanguagesVisibility()
    {
        LanguagesVisible = !LanguagesVisible;
    }

    [RelayCommand]
    private void ToggleAdvancedOptions()
    {
        ShowAdvancedOptions = !ShowAdvancedOptions;
    }

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to pre-translate.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        await _bulkActionService.PreTranslateAsync(AllTranslation);
        UpdateSummaryInfo();
        IsDirty = true;
        StatusText = "Pre-translation completed.";
    }

    [RelayCommand]
    private void GenerateStatisticsBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to generate statistics.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        var stats = _bulkActionService.GenerateStatistics(AllTranslation);
        StatusText = stats;
        _messageService.ShowMessage(stats);
    }

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

    [RelayCommand]
    private void NewLanguage()
    {
        var dialog = new LanguagePrompt("New Language", "Enter the translation language name below.", AllTranslation)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            AddLanguage(dialog.ResponseText);
        }
    }




    [RelayCommand]
    private void NewItem()
    {
        string ns = SelectedNode?.Namespace ?? "";

        var dialog = new PromptDialog("New Translation", "Please enter an ID for the translation\nUse '.' to create hierarchical IDs.", ns)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            CreateNewItem(dialog.ResponseText);
        }
    }



    [RelayCommand]
    private void RenameItem()
    {
        var node = SelectedNode;
        if (node == null) return;

        var dialog = new PromptDialog("Rename: " + node.Name, "Enter the new name below.", node.Name)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            RenameItem(node, dialog.ResponseText);
        }
    }

    [RelayCommand]
    private void DeleteItem()
    {
        var node = SelectedNode;
        if (node != null)
        {
            DeleteItem(node);
        }
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
            _projectService.CreateLanguage(CurrentPath, "en-US");
            LoadFolder(CurrentPath);
        }
    }
    [RelayCommand]
    private void OpenFolder()
    {
        string? selected = _dialogService.SelectFolder(CurrentPath);

        if (selected != null)
        {
            CurrentPath = selected;
            AppOptions.DefaultPath = selected;
            _recentFileService.Add(selected);
            LoadFolder(CurrentPath);
        }
        else
        {
            _messageService.ShowMessage("No folder selected.");
        }
    }


    private void LoadFolder(string path)
    {
        AllTranslation = _projectService.Load(path);
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
                _projectService.Save(CurrentPath, SaveStyles.Json, CurrentTreeItems.ToList(), AllTranslation);
                break;
            case SaveStyles.Namespaced:
                _projectService.Save(CurrentPath, SaveStyles.Namespaced, CurrentTreeItems.ToList(), AllTranslation);
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
        AllTranslation = new();
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
        var recents = _recentFileService.LoadRecent();
        if (recents.Count == 0)
        {
            _messageService.ShowMessage("No recent projects found.");
            return;
        }

        string path = recents?.First().Path; // For now, just use first. Replace with proper recent list picker later
        if (!Directory.Exists(path))
        {
            _messageService.ShowMessage($"Path not found: {path}");
            return;
        }

        CurrentPath = path;
        LoadFolder(CurrentPath);
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

    public void CreateNewItem(string newNamespace)
    {
        if (string.IsNullOrWhiteSpace(newNamespace))
            return;

        if (AllTranslation.NoEmpty().Any(setting => setting.Namespace.Contains(newNamespace)))
        {
            MessageBox.Show("Duplicate name");
            return;
        }

        var languages = AllTranslation.ToLanguages().ToList();
        foreach (string lang in languages)
        {
            AllTranslation.Add(new TranslationItem
            {
                Namespace = newNamespace,
                Value = string.Empty,
                Language = lang
            });
        }

        RefreshTree(newNamespace);
        UpdateSummaryInfo();
    }

    public void AddLanguage(string newLanguage)
    {
        if (string.IsNullOrWhiteSpace(newLanguage))
            return;

        if (AllTranslation.Any(setting => setting.Language == newLanguage))
        {
            MessageBox.Show("Duplicate language");
            return;
        }

        AllTranslation.Add(new TranslationItem
        {
            Namespace = "",
            Value = "",
            Language = newLanguage
        });

        AddMissingTranslations();
        UpdateSummaryInfo();
        RefreshTree();
    }

    public void RenameItem(NsTreeItem node, string newName)
    {
        if (node == null || string.IsNullOrWhiteSpace(newName) || newName.Contains('.'))
            return;

        string oldNs = node.Namespace;
        string newNs = oldNs[..oldNs.LastIndexOf(node.Name, StringComparison.InvariantCulture)] + newName.Trim();

        AllTranslation.ForParse().ToList().ForEach(item =>
        {
            if (item.Namespace.StartsWith(oldNs, StringComparison.InvariantCulture))
            {
                item.Namespace = item.Namespace.Replace(oldNs, newNs, StringComparison.InvariantCulture);
            }
        });

        RefreshTree(newNs);
    }

    public void DeleteItem(NsTreeItem node)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.Namespace))
            return;

        if (node.Parent == null)
        {
            CurrentTreeItems.Remove(node);
        }
        else if (node.Parent.Items is List<NsTreeItem> siblings)
        {
            siblings.Remove(node);
        }

        AllTranslation.RemoveAll(o => o?.Namespace?.StartsWith(node.Namespace) ?? false);
        RefreshTree();
    }


}

