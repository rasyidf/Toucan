using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class LanguageManagerViewModel : ObservableObject
{
    private readonly ProjectSettings _settings;

    public ObservableCollection<string> Languages { get; }
    [ObservableProperty] private string? selectedLanguage;
    [ObservableProperty] private string primaryLanguage;

    public bool IsPrimary => SelectedLanguage == PrimaryLanguage;

    public LanguageManagerViewModel(ProjectSettings settings)
    {
        _settings = settings;
        Languages = new ObservableCollection<string>(settings.Languages);
        primaryLanguage = settings.PrimaryLanguage;
    }

    public void AddLanguage(string lang)
    {
        if (!string.IsNullOrWhiteSpace(lang) && !Languages.Contains(lang))
            Languages.Add(lang);
    }

    [RelayCommand]
    private void RemoveLanguage(string lang)
    {
        if (Languages.Count > 1 && lang != PrimaryLanguage)
            Languages.Remove(lang);
    }

    public void MoveUp()
    {
        if (SelectedLanguage == null) return;
        var idx = Languages.IndexOf(SelectedLanguage);
        if (idx > 0) Languages.Move(idx, idx - 1);
    }

    public void MoveDown()
    {
        if (SelectedLanguage == null) return;
        var idx = Languages.IndexOf(SelectedLanguage);
        if (idx >= 0 && idx < Languages.Count - 1) Languages.Move(idx, idx + 1);
    }

    public void SetPrimary()
    {
        if (SelectedLanguage != null)
        {
            PrimaryLanguage = SelectedLanguage;
            OnPropertyChanged(nameof(IsPrimary));
        }
    }

    public void Save()
    {
        _settings.Languages = Languages.ToList();
        _settings.PrimaryLanguage = PrimaryLanguage;
        _settings.Save();
    }
}
