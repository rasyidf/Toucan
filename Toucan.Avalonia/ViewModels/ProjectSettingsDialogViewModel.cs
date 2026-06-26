using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class ProjectSettingsDialogViewModel : ObservableObject
{
    private readonly ProjectSettings _settings;

    [ObservableProperty] private string name;
    [ObservableProperty] private string primaryLanguage;
    [ObservableProperty] private string selectedFormat;
    [ObservableProperty] private bool saveEmptyTranslations;
    [ObservableProperty] private string translationOrder;
    [ObservableProperty] private string? defaultProvider;

    public ObservableCollection<string> Languages { get; }
    public ObservableCollection<string> Formats { get; } = ["Json", "Namespaced", "Yaml", "Toml", "AndroidXml", "IosStrings", "Xliff", "Arb", "Csv", "Resx", "Properties", "Adb"];
    public ObservableCollection<string> OrderOptions { get; } = ["alphabetical", "primary_language", "as-is"];

    public ProjectSettingsDialogViewModel(ProjectSettings settings)
    {
        _settings = settings;
        name = settings.Name ?? "";
        primaryLanguage = settings.PrimaryLanguage;
        selectedFormat = settings.SaveStyle.ToString();
        saveEmptyTranslations = settings.SaveEmptyTranslations;
        translationOrder = settings.TranslationOrder;
        defaultProvider = settings.DefaultProvider;
        Languages = new ObservableCollection<string>(settings.Languages);
    }

    public void AddLanguage(string lang)
    {
        if (!Languages.Contains(lang)) Languages.Add(lang);
    }

    [RelayCommand]
    private void RemoveLanguage(string lang)
    {
        if (Languages.Count > 1) Languages.Remove(lang);
    }

    public void Apply()
    {
        _settings.Name = Name;
        _settings.PrimaryLanguage = PrimaryLanguage;
        if (System.Enum.TryParse<SaveStyles>(SelectedFormat, out var style))
            _settings.SaveStyle = style;
        _settings.SaveEmptyTranslations = SaveEmptyTranslations;
        _settings.TranslationOrder = TranslationOrder;
        _settings.DefaultProvider = DefaultProvider;
        _settings.Languages = Languages.ToList();
        _settings.Save();
    }
}
