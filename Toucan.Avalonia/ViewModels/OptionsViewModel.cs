using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Avalonia.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;

    [ObservableProperty] private AppOptions appOptions;
    [ObservableProperty] private string defaultLanguage = "en-US";
    [ObservableProperty] private string pageSizeText = "100";
    [ObservableProperty] private string truncateSizeText = "2000";
    [ObservableProperty] private string maxItemsText = "100";

    public Action<bool?>? CloseAction { get; set; }

    public OptionsViewModel(IPreferenceService preferenceService)
    {
        _preferenceService = preferenceService;
        AppOptions = _preferenceService.Load();
        DefaultLanguage = AppOptions.DefaultLanguage;
        PageSizeText = AppOptions.PageSize.ToString();
        TruncateSizeText = AppOptions.TruncateResultsOver.ToString();
        MaxItemsText = AppOptions.MaxItems.ToString();
    }

    [RelayCommand]
    private void Save()
    {
        if (!int.TryParse(PageSizeText, out int page)) page = AppOptions.PageSize;
        if (!int.TryParse(TruncateSizeText, out int trunc)) trunc = AppOptions.TruncateResultsOver;
        if (!int.TryParse(MaxItemsText, out int maxItems)) maxItems = AppOptions.MaxItems;
        AppOptions.PageSize = page;
        AppOptions.TruncateResultsOver = trunc;
        AppOptions.MaxItems = maxItems;
        AppOptions.DefaultLanguage = DefaultLanguage;
        _preferenceService.Save(AppOptions);
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);
}
