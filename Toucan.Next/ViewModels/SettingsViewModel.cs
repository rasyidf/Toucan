 

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlzEx.Theming;
using Microsoft.Extensions.Options;
using System;
using System.Windows.Input;
using Toucan.Contracts.Services;
using Toucan.Contracts.ViewModels;
using Toucan.Models;

namespace Toucan.ViewModels;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly IAppConfigService _appConfigService;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ISystemService _systemService;
    private readonly IApplicationInfoService _applicationInfoService;

    public SettingsViewModel(
        IAppConfigService appConfigService,
        IThemeSelectorService themeSelectorService,
        ISystemService systemService,
        IApplicationInfoService applicationInfoService)
    { 
        _appConfigService = appConfigService;
        _themeSelectorService = themeSelectorService;
        _systemService = systemService;
        _applicationInfoService = applicationInfoService;
    }

    // === Automatically implements INotifyPropertyChanged with backing fields ===

    [ObservableProperty]
    private AppTheme theme;

    [ObservableProperty]
    private string versionDescription;

    [ObservableProperty]
    private bool autoSave;

    [ObservableProperty]
    private bool enableBackup;

    [ObservableProperty]
    private bool autoCapitalize;

    [ObservableProperty]
    private bool useCompactLayout;

    [ObservableProperty]
    private bool enableDevTools;

    [ObservableProperty]
    private string selectedFontSize = "Normal";

    [ObservableProperty]
    private string selectedKeyStyle = "dot.notation";

    // === RelayCommand replaces manual ICommand ===

    [RelayCommand]
    private void SetTheme(string themeName)
    {
        if (!string.IsNullOrWhiteSpace(themeName) &&
            Enum.TryParse(themeName, out AppTheme parsedTheme))
        {
            _themeSelectorService.SetTheme(parsedTheme);
            Theme = parsedTheme;
        }
    }

    [RelayCommand]
    private void PrivacyStatement()
    {
        _systemService.OpenInWebBrowser(_appConfigService.Current.PrivacyStatement);
    }

    [RelayCommand]
    private void ExportSettings()
    {
        // TODO: implement export to JSON or user storage
    }

    [RelayCommand]
    private void ResetSettings()
    {
        SelectedFontSize = "Normal";
        SelectedKeyStyle = "dot.notation";
        AutoSave = true;
        EnableBackup = false;
        AutoCapitalize = true;
        UseCompactLayout = false;
        EnableDevTools = false;

        Theme = AppTheme.Light;
        _themeSelectorService.SetTheme(Theme);
    }

    public void OnNavigatedTo(object parameter)
    {
        VersionDescription = $"{Properties.Resources.AppDisplayName} - {_applicationInfoService.GetVersion()}";
        Theme = _themeSelectorService.GetCurrentTheme();
        LoadSettings();
    }

    public void OnNavigatedFrom()
    {
        SaveSettings();
    }


    private void LoadSettings()
    {
        var config = _appConfigService.Current;

        AutoSave = config.AutoSave;
        EnableBackup = config.EnableBackup;
        AutoCapitalize = config.AutoCapitalize;
        UseCompactLayout = config.UseCompactLayout;
        EnableDevTools = config.EnableDevTools;
        SelectedFontSize = config.SelectedFontSize;
        SelectedKeyStyle = config.SelectedKeyStyle;
    }

    private void SaveSettings()
    {
        var config = _appConfigService.Current;

        config.AutoSave = AutoSave;
        config.EnableBackup = EnableBackup;
        config.AutoCapitalize = AutoCapitalize;
        config.UseCompactLayout = UseCompactLayout;
        config.EnableDevTools = EnableDevTools;
        config.SelectedFontSize = SelectedFontSize;
        config.SelectedKeyStyle = SelectedKeyStyle;

        _appConfigService.Save();
    }
}
