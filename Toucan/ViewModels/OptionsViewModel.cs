using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Windows.Input;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

internal partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;

    public OptionsViewModel(IPreferenceService preferenceService)
    {
        _preferenceService = preferenceService;
        AppOptions = _preferenceService.Load();

        // initialize vm fields
        Indent = "tab";
        Format = AppOptions.SaveStyle.ToString();
        PageSizeText = AppOptions.PageSize.ToString();
        TruncateSizeText = AppOptions.TruncateResultsOver.ToString();
    }

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

    public int ContextCharCount => Context?.Length ?? 0;

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(PageSizeText, out int page)) page = AppOptions.PageSize;
        if (!int.TryParse(TruncateSizeText, out int trunc)) trunc = AppOptions.TruncateResultsOver;

        if (Enum.TryParse<Core.Models.SaveStyles>(Format, out var ss))
            AppOptions.SaveStyle = ss;

        AppOptions.PageSize = page;
        AppOptions.TruncateResultsOver = trunc;

        _preferenceService.Save(AppOptions);

        // close window by setting result (owner handles)
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    public Action<bool?> CloseAction { get; set; }

    [RelayCommand]
    private void ConfigureLanguageCodes()
    {
        // future: implement language code configuration
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
}
