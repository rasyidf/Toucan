using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class ProviderSettingsViewModel : ObservableObject
{
    private readonly IProviderSettingsService _service;
    private readonly IDialogService _dialogs;

    public ObservableCollection<ProviderSettings> Providers { get; } = new();
    [ObservableProperty] private ProviderSettings? selected;
    [ObservableProperty] private bool projectScope;
    [ObservableProperty] private string projectPath = string.Empty;

    public ProviderSettingsViewModel(IProviderSettingsService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        LoadAppSettings();
    }

    [RelayCommand]
    private void LoadAppSettings()
    {
        Providers.Clear();
        foreach (var p in _service.LoadAppProviderSettings()) Providers.Add(p);
        Selected = Providers.FirstOrDefault();
        ProjectScope = false;
    }

    [RelayCommand]
    private async Task LoadProjectSettings()
    {
        var path = ProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var folder = await _dialogs.SelectFolderAsync(Environment.CurrentDirectory);
            if (!string.IsNullOrEmpty(folder)) path = folder;
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        Providers.Clear();
        foreach (var p in _service.LoadProjectProviderSettings(path)) Providers.Add(p);
        Selected = Providers.FirstOrDefault();
        ProjectScope = true;
        ProjectPath = path;
    }

    [RelayCommand]
    private void Save()
    {
        if (ProjectScope) { if (!string.IsNullOrWhiteSpace(ProjectPath)) _service.SaveProjectProviderSettings(ProjectPath, Providers); }
        else _service.SaveAppProviderSettings(Providers);
    }

    [RelayCommand]
    private void AddProvider()
    {
        var newP = new ProviderSettings { Provider = "Custom", Options = new Dictionary<string, string>(), Secrets = new Dictionary<string, string>() };
        Providers.Add(newP);
        Selected = newP;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected == null) return;
        Providers.Remove(Selected);
        Selected = Providers.FirstOrDefault();
    }
}
