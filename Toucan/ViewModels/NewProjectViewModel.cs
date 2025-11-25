using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Toucan.Services;
using System;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    [ObservableProperty]
    private string projectName = string.Empty;

    [ObservableProperty]
    private string projectFolder = string.Empty;

    [ObservableProperty]
    private string selectedFramework = "JSON";

    public ObservableCollection<string> Frameworks { get; } = new()
    {
        "JSON",
        "INI",
        "PO",
        "YAML"
    };

    [ObservableProperty]
    private string sourceLanguage = "en-US";

    [ObservableProperty]
    private ObservableCollection<string> languages = new();

    public NewProjectViewModel(IProjectService projectService = null)
    {
        _projectService = projectService;
        
        // default language
        Languages.Add("en-US");
    }

    [RelayCommand]
    private void AddLanguage()
    {
        // Uses the existing LanguagePrompt to ask for language
        var dialog = new LanguagePrompt("Add Language", "Pick a language to add", null);
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var language = dialog.ResponseText;
            if (!string.IsNullOrWhiteSpace(language) && !Languages.Contains(language))
            {
                Languages.Add(language);
            }
        }
    }

    [RelayCommand]
    private void RemoveLanguage(string language)
    {
        if (string.IsNullOrEmpty(language)) return;
        if (Languages.Count <= 1)
        {
            // Keep at least one language
            return;
        }
        Languages.Remove(language);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var ds = new DialogService();
        var selected = ds.SelectFolder(ProjectFolder);
        if (selected != null)
        {
            ProjectFolder = selected;
        }
    }

    public bool IsValid()
    {
        // Basic validation: project name & folder
        if (string.IsNullOrWhiteSpace(ProjectName) || string.IsNullOrWhiteSpace(ProjectFolder))
            return false;

        // For now, we don't require file paths to be set manually
        // since CreateProject will generate them automatically
        return true;
    }

    /// <summary>
    /// Creates the actual project files on disk
    /// </summary>
    public void CreateProject()
    {
        if (_projectService == null)
            throw new InvalidOperationException("ProjectService is not available");

        if (!IsValid())
            throw new InvalidOperationException("Project settings are not valid");

        // Map framework selection to SaveStyles
        SaveStyles style = SelectedFramework switch
        {
            "INI" => SaveStyles.Adb,
            "PO" => SaveStyles.Properties,
            "YAML" => SaveStyles.Yaml,
            _ => SaveStyles.Json
        };

        // Create the project folder if it doesn't exist and create language files
        _projectService.CreateProject(ProjectFolder, Languages, style);
    }
}
