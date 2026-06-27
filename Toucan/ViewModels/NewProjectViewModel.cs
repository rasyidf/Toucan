using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

/// <summary>Framework tile shown in New Project grid.</summary>
public class FrameworkTile
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "🌐";
    public SaveStyles Style { get; init; } = SaveStyles.Json;
    /// <summary>Framework profile ID (matches IFrameworkProfile.Id). Null = use Style-based fallback.</summary>
    public string? ProfileId { get; init; }
}

public partial class NewProjectViewModel : ObservableObject
{
    private readonly IProjectService? _projectService;
    private readonly IDialogService? _dialogService;

    [ObservableProperty]
    private string projectName = string.Empty;

    [ObservableProperty]
    private string projectFolder = string.Empty;

    [ObservableProperty]
    private FrameworkTile? selectedFramework;

    // Wizard step: 0 = template+title, 1 = languages
    [ObservableProperty]
    private int wizardStep = 0;

    public bool IsStep0 => WizardStep == 0;
    public bool IsStep1 => WizardStep == 1;

    public ObservableCollection<FrameworkTile> Frameworks { get; } =
    [
        new() { Name = "i18next", Description = "JSON namespaced", Icon = "⚙", Style = SaveStyles.Namespaced, ProfileId = "i18next" },
        new() { Name = "React", Description = "Flat JSON", Icon = "⚛", Style = SaveStyles.Json, ProfileId = "generic-json" },
        new() { Name = "Vue", Description = "vue-i18n JSON", Icon = "🟢", Style = SaveStyles.Json, ProfileId = "generic-json" },
        new() { Name = "Angular", Description = "JSON / XLIFF", Icon = "🅰", Style = SaveStyles.Json },
        new() { Name = "Flutter", Description = "ARB format", Icon = "🐦", Style = SaveStyles.Arb },
        new() { Name = "Laravel", Description = "PHP / JSON", Icon = "🔷", Style = SaveStyles.LaravelPhp },
        new() { Name = ".NET", Description = "RESX resource", Icon = "🟣", Style = SaveStyles.Resx },
        new() { Name = "Android", Description = "strings.xml", Icon = "🤖", Style = SaveStyles.AndroidXml, ProfileId = "android" },
        new() { Name = "iOS", Description = ".strings", Icon = "🍎", Style = SaveStyles.IosStrings },
        new() { Name = "Ruby/Rails", Description = "YAML locale", Icon = "💎", Style = SaveStyles.Yaml },
        new() { Name = "Svelte", Description = "svelte-i18n", Icon = "🔶", Style = SaveStyles.Json },
        new() { Name = "Java", Description = ".properties", Icon = "☕", Style = SaveStyles.JavaProperties },
        new() { Name = "Gettext", Description = "PO files", Icon = "📝", Style = SaveStyles.Properties },
        new() { Name = "Generic JSON", Description = "Flat JSON", Icon = "{ }", Style = SaveStyles.Json, ProfileId = "generic-json" },
        new() { Name = "Generic YAML", Description = "YAML files", Icon = "≡", Style = SaveStyles.Yaml },
        new() { Name = "CSV", Description = "CSV table", Icon = "📊", Style = SaveStyles.Csv },
    ];

    [ObservableProperty]
    private string sourceLanguage = "en-US";

    [ObservableProperty]
    private ObservableCollection<string> languages = [];

    [ObservableProperty]
    private bool createManifest = false;

    public bool IsValid => !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(ProjectFolder) && Languages?.Count > 0 && SelectedFramework != null;

    public NewProjectViewModel(IProjectService? projectService = null, IDialogService? dialogService = null)
    {
        _projectService = projectService;
        _dialogService = dialogService;
        SelectedFramework = Frameworks[0];

        try
        {
            AppOptions opts = Toucan.Core.Options.AppOptions.LoadFromDisk();
            var defaultLang = string.IsNullOrWhiteSpace(opts?.DefaultLanguage) ? "en-US" : opts.DefaultLanguage;
            SourceLanguage = defaultLang;
            Languages.Add(defaultLang);
        }
        catch
        {
            Languages.Add("en-US");
        }

        Languages?.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsValid));
    }

    [RelayCommand]
    private void NextStep()
    {
        if (WizardStep == 0 && SelectedFramework != null && !string.IsNullOrWhiteSpace(ProjectFolder))
        {
            // Auto-set project name from framework if empty
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                ProjectName = SelectedFramework.Name + "-i18n";
            }

            WizardStep = 1;
            OnPropertyChanged(nameof(IsStep0));
            OnPropertyChanged(nameof(IsStep1));
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (WizardStep > 0)
        {
            WizardStep = 0;
            OnPropertyChanged(nameof(IsStep0));
            OnPropertyChanged(nameof(IsStep1));
        }
    }

    [RelayCommand]
    private void AddLanguage()
    {
        var result = _dialogService?.ShowLanguagePrompt("Add Language", "Enter a language code", null);
        if (!string.IsNullOrWhiteSpace(result) && !Languages.Contains(result))
        {
            Languages.Add(result);
        }
    }

    [RelayCommand]
    private void AddDefaults()
    {
        try
        {
            AppOptions opts = Toucan.Core.Options.AppOptions.LoadFromDisk();
            var suggested = opts?.SuggestedLanguages ?? ["en-US", "id-ID", "zh-CN", "fr-FR", "es-ES"];
            foreach (var d in suggested)
            {
                if (!Languages.Contains(d))
                {
                    Languages.Add(d);
                }
            }
        }
        catch
        {
            if (!Languages.Contains("en-US"))
            {
                Languages.Add("en-US");
            }
        }
    }

    [RelayCommand]
    private void RemoveLanguage(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return;
        }

        if (Languages.Count <= 1)
        {
            return;
        }

        _ = Languages.Remove(language);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var selected = _dialogService?.SelectFolder(ProjectFolder);
        if (selected != null)
        {
            ProjectFolder = selected;
        }
    }

    partial void OnProjectNameChanged(string value) => OnPropertyChanged(nameof(IsValid));
    partial void OnProjectFolderChanged(string value) => OnPropertyChanged(nameof(IsValid));

    partial void OnSelectedFrameworkChanged(FrameworkTile? value)
    {
        OnPropertyChanged(nameof(IsValid));
        // Auto-set project name based on framework
        if (value != null && string.IsNullOrWhiteSpace(ProjectName))
            ProjectName = value.Name + "-i18n";
    }

    partial void OnLanguagesChanged(ObservableCollection<string> value)
    {
        if (value != null)
            value.CollectionChanged += (s, e) => OnPropertyChanged(nameof(IsValid));
    }

    public void CreateProject()
    {
        if (_projectService == null)
        {
            throw new InvalidOperationException("ProjectService is not available");
        }

        if (!IsValid)
        {
            throw new InvalidOperationException("Project settings are not valid");
        }

        var style = SelectedFramework?.Style ?? SaveStyles.Json;
        var settings = _projectService.CreateProject(ProjectFolder, Languages, style, CreateManifest, ProjectName);

        // Persist framework profile ID in manifest if available
        if (SelectedFramework?.ProfileId != null && settings != null)
        {
            settings.Framework = SelectedFramework.ProfileId;
            settings.Save();
        }
    }
}
