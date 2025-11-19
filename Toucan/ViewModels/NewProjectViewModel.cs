using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Toucan.Services;
using System;

namespace Toucan.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
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

    [ObservableProperty]
    private ObservableCollection<Toucan.TranslationPackage> packages = new();

    public NewProjectViewModel()
    {
        // Add a default package
        Packages.Add(new Toucan.TranslationPackage("main"));
        // default language
        Languages.Add("en-US");
        // Setup default translation entry for default package
        foreach (var p in Packages)
            p.Translations.Add(new Toucan.TranslationEntry { Language = "en-US", FilePath = string.Empty });
    }

    [RelayCommand]
    private void AddPackage()
    {
        string name = $"Package {Packages.Count + 1}";
        var p = new Toucan.TranslationPackage(name);
        foreach (var lang in Languages)
            p.Translations.Add(new Toucan.TranslationEntry { Language = lang, FilePath = string.Empty });
        Packages.Add(p);
    }

    [RelayCommand]
    private void RemovePackage(Toucan.TranslationPackage package)
    {
        if (package != null)
            Packages.Remove(package);
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
                // Add language file entry to each package
                foreach (var p in Packages)
                {
                    if (!p.Translations.Any(t => t.Language == language))
                        p.Translations.Add(new Toucan.TranslationEntry { Language = language, FilePath = string.Empty });
                }
            }
        }
    }

    [RelayCommand]
    private void RemoveLanguage(string language)
    {
        if (string.IsNullOrEmpty(language)) return;
        Languages.Remove(language);
        foreach (var p in Packages)
        {
            var entry = p.Translations.FirstOrDefault(t => t.Language == language);
            if (entry != null)
                p.Translations.Remove(entry);
        }
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

    [RelayCommand]
    private void BrowseTranslationFile(object param)
    {
        if (param is not string composite) return;

        // param will be "packageIndex|language"
        var parts = composite.Split('|');
        if (parts.Length != 2) return;

        if (!int.TryParse(parts[0], out int packageIndex)) return;
        string lang = parts[1];
        if (packageIndex < 0 || packageIndex >= Packages.Count) return;

        var initial = string.IsNullOrEmpty(ProjectFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : ProjectFolder;
        var ds = new DialogService();
        string filter = SelectedFramework switch
        {
            "YAML" => "YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*",
            "PO" => "PO files (*.po)|*.po|All files (*.*)|*.*",
            "INI" => "INI files (*.ini)|*.ini|All files (*.*)|*.*",
            _ => "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        var file = ds.SelectFile(initial, filter);
        if (!string.IsNullOrWhiteSpace(file))
        {
            var pkg = Packages[packageIndex];
            var entry = pkg.Translations.FirstOrDefault(t => t.Language == lang);
            if (entry != null)
                entry.FilePath = file;
            // Notify UI
            OnPropertyChanged(nameof(Packages));
        }
    }

    public bool IsValid()
    {
        // Basic validation: project name & folder
        if (string.IsNullOrWhiteSpace(ProjectName) || string.IsNullOrWhiteSpace(ProjectFolder))
            return false;

        // ensure all language file paths are set per package
        foreach (var p in Packages)
        {
            foreach (var lang in Languages)
            {
                    var ent = p.Translations.FirstOrDefault(t => t.Language == lang);
                    if (ent == null) return false;
                    if (string.IsNullOrWhiteSpace(ent.FilePath)) return false;
            }
        }
        return true;
    }
}
