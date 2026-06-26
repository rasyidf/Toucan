using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Avalonia.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
    private readonly IProjectService? _projectService;
    private readonly IDialogService? _dialogService;

    [ObservableProperty] private string projectName = string.Empty;
    [ObservableProperty] private string projectFolder = string.Empty;
    [ObservableProperty] private string selectedFramework = "JSON";
    [ObservableProperty] private string sourceLanguage = "en-US";
    [ObservableProperty] private ObservableCollection<string> languages = new();
    [ObservableProperty] private bool createManifest;

    public ObservableCollection<string> Frameworks { get; } = ["JSON", "INI", "PO", "YAML"];
    public bool IsValid => !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(ProjectFolder) && Languages?.Count > 0;

    public NewProjectViewModel(IProjectService? projectService = null, IDialogService? dialogService = null)
    {
        _projectService = projectService;
        _dialogService = dialogService;
        var opts = AppOptions.LoadFromDisk();
        SourceLanguage = string.IsNullOrWhiteSpace(opts?.DefaultLanguage) ? "en-US" : opts.DefaultLanguage;
        Languages.Add(SourceLanguage);
        foreach (var s in new[] { "en-US", "id-ID", "zh-CN", "fr-FR" })
            if (!Languages.Contains(s)) Languages.Add(s);
        Languages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsValid));
    }

    [RelayCommand] private void AddDefaults()
    {
        foreach (var d in new[] { "en-US", "id-ID", "zh-CN", "fr-FR", "es-ES" })
            if (!Languages.Contains(d)) Languages.Add(d);
    }

    [RelayCommand] private void RemoveLanguage(string language)
    {
        if (string.IsNullOrEmpty(language) || Languages.Count <= 1) return;
        Languages.Remove(language);
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (_dialogService == null) return;
        var selected = await _dialogService.SelectFolderAsync(ProjectFolder);
        if (selected != null) ProjectFolder = selected;
    }

    partial void OnProjectNameChanged(string value) => OnPropertyChanged(nameof(IsValid));
    partial void OnProjectFolderChanged(string value) => OnPropertyChanged(nameof(IsValid));

    public void CreateProject()
    {
        if (_projectService == null) throw new InvalidOperationException("ProjectService is not available");
        if (!IsValid) throw new InvalidOperationException("Project settings are not valid");
        SaveStyles style = SelectedFramework switch
        {
            "INI" => SaveStyles.Adb,
            "PO" => SaveStyles.Properties,
            "YAML" => SaveStyles.Yaml,
            _ => SaveStyles.Json
        };
        _projectService.CreateProject(ProjectFolder, Languages, style, CreateManifest);
    }
}
