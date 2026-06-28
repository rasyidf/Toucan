using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Toucan.Core;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

/// <summary>
/// ViewModel for the Manage Languages dialog. Allows add, remove, reorder, and set primary language.
/// </summary>
public partial class LanguageManagerViewModel : ObservableObject
{
    private readonly List<TranslationItem> _allTranslations;

    [ObservableProperty]
    private ObservableCollection<LanguageEntry> languages = [];

    [ObservableProperty]
    private LanguageEntry? selectedLanguage;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LanguageModel> filteredCultures = [];

    private readonly List<LanguageModel> _allCultures;

    /// <summary>Languages that were removed during this session (caller uses this to delete translation data).</summary>
    public List<string> RemovedLanguages { get; } = [];

    /// <summary>Languages that were added during this session.</summary>
    public List<string> AddedLanguages { get; } = [];

    /// <summary>The primary language after edits.</summary>
    public string PrimaryLanguage => Languages.FirstOrDefault(l => l.IsPrimary)?.Code ?? Languages.FirstOrDefault()?.Code ?? "en-US";

    public LanguageManagerViewModel(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null)
    {
        _allTranslations = allTranslations?.ToList() ?? [];

        // Build current language list from translations
        var existingLanguages = _allTranslations
            .Select(t => t.Language)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var lang in existingLanguages)
        {
            Languages.Add(new LanguageEntry
            {
                Code = lang,
                DisplayName = GetDisplayName(lang),
                IsPrimary = string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase),
                TranslationCount = _allTranslations.Count(t => t.Language == lang && !string.IsNullOrEmpty(t.Value)),
                TotalKeys = _allTranslations.Count(t => t.Language == lang)
            });
        }

        // If no primary was set, default to first
        if (!Languages.Any(l => l.IsPrimary) && Languages.Count > 0)
        {
            Languages[0].IsPrimary = true;
        }

        // Build culture list for adding new languages
        var existingSet = new HashSet<string>(existingLanguages, StringComparer.OrdinalIgnoreCase);
        _allCultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .OrderBy(c => c.DisplayName)
            .Select(c => new LanguageModel { Culture = c, Language = c.DisplayName })
            .Where(l => !existingSet.Contains(l.Culture?.Name ?? ""))
            .ToList();

        FilteredCultures = new ObservableCollection<LanguageModel>(_allCultures.Take(50));
    }

    partial void OnFilterTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FilteredCultures = new ObservableCollection<LanguageModel>(_allCultures.Take(50));
            return;
        }

        var filtered = _allCultures
            .Where(c => c.Language.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                       (c.Culture?.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (c.Culture?.NativeName.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(50);

        FilteredCultures = new ObservableCollection<LanguageModel>(filtered);
    }

    [RelayCommand]
    private void AddLanguage(LanguageModel? model)
    {
        if (model?.Culture == null)
        {
            return;
        }

        string code = model.Culture.Name;
        if (Languages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var entry = new LanguageEntry
        {
            Code = code,
            DisplayName = model.Language,
            IsPrimary = false,
            TranslationCount = 0,
            TotalKeys = 0
        };

        Languages.Add(entry);
        AddedLanguages.Add(code);

        // Remove from available cultures
        _allCultures.Remove(model);
        OnFilterTextChanged(FilterText);
    }

    public void AddLanguageByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (Languages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var entry = new LanguageEntry
        {
            Code = code,
            DisplayName = GetDisplayName(code),
            IsPrimary = false,
            TranslationCount = 0,
            TotalKeys = 0
        };

        Languages.Add(entry);
        AddedLanguages.Add(code);
        OnFilterTextChanged(FilterText);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveLanguage))]
    private void RemoveLanguage(LanguageEntry? entry)
    {
        if (entry == null || entry.IsPrimary)
        {
            return;
        }

        Languages.Remove(entry);
        RemovedLanguages.Add(entry.Code);

        // Add back to available cultures
        try
        {
            var culture = CultureInfo.GetCultureInfo(entry.Code);
            _allCultures.Add(new LanguageModel { Culture = culture, Language = culture.DisplayName });
            _allCultures.Sort((a, b) => string.Compare(a.Language, b.Language, StringComparison.OrdinalIgnoreCase));
        }
        catch { /* custom code, not a known culture */ }

        OnFilterTextChanged(FilterText);
    }

    private bool CanRemoveLanguage(LanguageEntry? entry)
    {
        return entry != null && !entry.IsPrimary && Languages.Count > 1;
    }

    [RelayCommand]
    private void SetPrimary(LanguageEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        foreach (var lang in Languages)
        {
            lang.IsPrimary = lang == entry;
        }
    }

    [RelayCommand]
    private void MoveUp(LanguageEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        int index = Languages.IndexOf(entry);
        if (index > 0)
        {
            Languages.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(LanguageEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        int index = Languages.IndexOf(entry);
        if (index >= 0 && index < Languages.Count - 1)
        {
            Languages.Move(index, index + 1);
        }
    }

    private static string GetDisplayName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.DisplayName;
        }
        catch
        {
            return code;
        }
    }
}

/// <summary>
/// Represents a single language entry in the manager.
/// </summary>
public partial class LanguageEntry : ObservableObject
{
    [ObservableProperty]
    private string code = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private bool isPrimary;

    [ObservableProperty]
    private int translationCount;

    [ObservableProperty]
    private int totalKeys;

    public string Summary => TotalKeys > 0 ? $"{TranslationCount}/{TotalKeys} translated" : "No keys";
}
