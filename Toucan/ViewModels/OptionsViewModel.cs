using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;
    private readonly IDialogService _dialogService;

    public OptionsViewModel(IPreferenceService preferenceService, IDialogService dialogService)
    {
        _preferenceService = preferenceService;
        _dialogService = dialogService;
        AppOptions = _preferenceService.Load();

        // initialize vm fields
        Indent = "tab";
        Format = "Json";
        PageSizeText = AppOptions.PageSize.ToString();
        TruncateSizeText = AppOptions.TruncateResultsOver.ToString();
        MaxItemsText = AppOptions.MaxItems.ToString();
        DefaultLanguage = AppOptions.DefaultLanguage ?? "en-US";
        Theme = AppOptions.Theme ?? "System";
        AppLanguage = AppOptions.AppLanguage ?? "en-US";
        PlainTextKeys = AppOptions.PlainTextKeys;
        CopyTemplate1 = AppOptions.CopyTemplate1 ?? "%1";
        CopyTemplate2 = AppOptions.CopyTemplate2 ?? string.Empty;
        CopyTemplate3 = AppOptions.CopyTemplate3 ?? string.Empty;
        Formality = AppOptions.Formality ?? "Default";
        Context = AppOptions.Context ?? string.Empty;
        SuggestedLanguages = new System.Collections.ObjectModel.ObservableCollection<string>(AppOptions.SuggestedLanguages ?? ["en-US"]);
    }

    [ObservableProperty]
    private string defaultLanguage;

    [ObservableProperty]
    private string theme;

    [ObservableProperty]
    private string appLanguage;

    [ObservableProperty]
    private bool plainTextKeys;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<string> suggestedLanguages = [];

    [RelayCommand]
    private void RemoveSuggestedLanguage(string lang)
    {
        if (!string.IsNullOrEmpty(lang))
        {
            _ = SuggestedLanguages.Remove(lang);
        }
    }

    // Project-specific editor configuration (if a project manifest is available)
    [ObservableProperty]
    private string projectFilePath = string.Empty;

    [ObservableProperty]
    private string projectPrimaryLanguage = string.Empty;

    [ObservableProperty]
    private bool projectSaveEmptyTranslations = true;

    [ObservableProperty]
    private string projectTranslationOrder = "Alphabetically sorted";

    [ObservableProperty]
    private string projectCopyTemplate1 = string.Empty;

    [ObservableProperty]
    private string projectCopyTemplate2 = string.Empty;

    [ObservableProperty]
    private string projectCopyTemplate3 = string.Empty;

    [ObservableProperty]
    private bool applyChangesToProject = false;

    [ObservableProperty]
    private AppOptions appOptions;

    [ObservableProperty]
    private string indent;

    [ObservableProperty]
    private string format;

    [ObservableProperty]
    private bool supportArrays;

    [ObservableProperty]
    private bool saveEmptyTranslations = true;

    [ObservableProperty]
    private string translationOrder = "Alphabetically sorted";

    [ObservableProperty]
    private string copyTemplate1 = "%1";

    [ObservableProperty]
    private string copyTemplate2 = string.Empty;

    [ObservableProperty]
    private string copyTemplate3 = string.Empty;

    [ObservableProperty]
    private string sourceRoot = string.Empty;

    [ObservableProperty]
    private string sourceCodeEditor = string.Empty;

    [ObservableProperty]
    private string editorParameters = string.Empty;

    [ObservableProperty]
    private string formality = "Default";

    [ObservableProperty]
    private string context = string.Empty;

    [ObservableProperty]
    private string pageSizeText;

    [ObservableProperty]
    private string truncateSizeText;

    [ObservableProperty]
    private string maxItemsText;

    public int ContextCharCount => Context?.Length ?? 0;

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(PageSizeText, out int page))
        {
            page = AppOptions.PageSize;
        }

        if (!int.TryParse(TruncateSizeText, out int trunc))
        {
            trunc = AppOptions.TruncateResultsOver;
        }

        if (!int.TryParse(MaxItemsText, out int maxItems))
        {
            maxItems = AppOptions.MaxItems;
        }

        if (Enum.TryParse(Format, out SaveStyles ss))
        { } // ponytail: SaveStyle is now per-project setting

        AppOptions.PageSize = page;
        AppOptions.TruncateResultsOver = trunc;
        AppOptions.MaxItems = maxItems;
        AppOptions.DefaultLanguage = DefaultLanguage;
        AppOptions.Theme = Theme;
        AppOptions.AppLanguage = AppLanguage;
        AppOptions.PlainTextKeys = PlainTextKeys;
        AppOptions.CopyTemplate1 = CopyTemplate1;
        AppOptions.CopyTemplate2 = CopyTemplate2;
        AppOptions.CopyTemplate3 = CopyTemplate3;
        AppOptions.Formality = Formality;
        AppOptions.Context = Context;
        AppOptions.SuggestedLanguages = [.. SuggestedLanguages];

        _preferenceService.Save(AppOptions);

        // If project changes are requested, attempt to write to the project's toucan.project manifest
        if (ApplyChangesToProject && !string.IsNullOrWhiteSpace(ProjectFilePath))
        {
            try
            {
                var manifestPath = System.IO.Path.Combine(ProjectFilePath, "toucan.project");
                if (System.IO.File.Exists(manifestPath))
                {
                    var text = System.IO.File.ReadAllText(manifestPath);
                    JsonObject root = System.Text.Json.Nodes.JsonNode.Parse(text)?.AsObject()
                        ?? [];

                    JsonObject editorCfg = root["editorConfiguration"]?.AsObject() ?? [];
                    editorCfg["save_empty_translations"] = ProjectSaveEmptyTranslations.ToString().ToLowerInvariant();
                    editorCfg["translation_order"] = ProjectTranslationOrder == "Primary language" ? "primary_language" : "alphabetical";
                    editorCfg["copy_templates"] = new JsonArray(ProjectCopyTemplate1 ?? string.Empty, ProjectCopyTemplate2 ?? string.Empty, ProjectCopyTemplate3 ?? string.Empty);

                    // attach back
                    root["editorConfiguration"] = editorCfg;

                    System.IO.File.WriteAllText(manifestPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch
            {
                // ignore manifest write failures here; UI may show message later
            }
        }

        // close window by setting result (owner handles)
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    public Action<bool?>? CloseAction { get; set; }

    [RelayCommand]
    private void ConfigureLanguageCodes()
    {
        // future: implement language code configuration
    }

    [RelayCommand]
    private void OpenProviderSettings()
    {
        _ = _dialogService.ShowProviderSettings();
    }

    [RelayCommand]
    private void BrowseSourceRoot()
    {
        // future: implement folder picker
    }

    [RelayCommand]
    private void BrowseSourceEditor()
    {
        // future: implement file picker
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    [RelayCommand]
    private void CopyVersion()
    {
        try { System.Windows.Clipboard.SetText("Toucan 0.10.0"); } catch { }
    }

    [RelayCommand]
    private void OpenLogLocation()
    {
        var logDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logDir) { UseShellExecute = true }); }
        catch { }
    }
}
