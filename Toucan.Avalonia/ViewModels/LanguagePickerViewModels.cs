using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Culture list helpers shared by the language pickers.</summary>
internal static class Cultures
{
    public static List<LanguageModel> All(IEnumerable<string>? exclude = null)
    {
        var existing = new HashSet<string>(exclude ?? [], StringComparer.OrdinalIgnoreCase);
        return CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(c => !existing.Contains(c.Name))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCulture)
            .Select(c => new LanguageModel { Culture = c, Language = c.DisplayName })
            .ToList();
    }

    public static bool Matches(LanguageModel m, string filter) =>
        string.IsNullOrWhiteSpace(filter)
        || m.Language.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (m.Culture?.NativeName.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || (m.Culture?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    public static string DisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (CultureNotFoundException) { return code; }
    }
}

/// <summary>Searchable list of cultures for adding a single language.</summary>
public partial class LanguagePromptViewModel : ObservableObject
{
    private readonly List<LanguageModel> _all;

    public LanguagePromptViewModel(IEnumerable<TranslationItem>? existingTranslations = null)
    {
        _all = Cultures.All(existingTranslations?.Select(t => t.Language));
        FilteredCultures = new ObservableCollection<LanguageModel>(_all);
    }

    public string Title { get; set; } = "Add Language";
    public string Message { get; set; } = "Search for a language, or type a custom code.";

    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private ObservableCollection<LanguageModel> filteredCultures;
    [ObservableProperty] private LanguageModel? selectedLanguage;

    /// <summary>The chosen code: the selected culture, or the raw text for custom codes.</summary>
    public string? Result => SelectedLanguage?.Culture?.Name ?? (string.IsNullOrWhiteSpace(FilterText) ? null : FilterText.Trim());

    public bool CanAccept => Result != null;

    partial void OnFilterTextChanged(string value)
    {
        FilteredCultures = new ObservableCollection<LanguageModel>(_all.Where(m => Cultures.Matches(m, value)));
        var exact = _all.FirstOrDefault(l =>
            string.Equals(l.Culture?.Name, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Language, value, StringComparison.OrdinalIgnoreCase));
        if (exact != null) SelectedLanguage = exact;
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(CanAccept));
    }

    partial void OnSelectedLanguageChanged(LanguageModel? value)
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(CanAccept));
    }
}

/// <summary>Manage Languages dialog: add, remove, reorder, and set the primary language.</summary>
public partial class LanguageManagerViewModel : ObservableObject
{
    private readonly List<LanguageModel> _allCultures;

    public LanguageManagerViewModel(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null)
    {
        var translations = allTranslations?.ToList() ?? [];
        var existing = translations.Select(t => t.Language)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var lang in existing)
        {
            var isPrimary = string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase);
            Languages.Add(new LanguageEntry
            {
                Code = lang,
                DisplayName = Cultures.DisplayName(lang),
                IsPrimary = isPrimary,
                TranslationCount = translations.Count(t => t.Language == lang && !string.IsNullOrWhiteSpace(t.Namespace) && !string.IsNullOrEmpty(t.Value)),
                TotalKeys = translations.Count(t => t.Language == lang && !string.IsNullOrWhiteSpace(t.Namespace))
            });
        }

        if (!Languages.Any(l => l.IsPrimary) && Languages.Count > 0) Languages[0].IsPrimary = true;

        _allCultures = Cultures.All(existing);
        FilteredCultures = new ObservableCollection<LanguageModel>(_allCultures.Take(100));
    }

    public ObservableCollection<LanguageEntry> Languages { get; } = [];

    [ObservableProperty] private LanguageEntry? selectedLanguage;
    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private ObservableCollection<LanguageModel> filteredCultures;
    [ObservableProperty] private LanguageModel? selectedCulture;

    public List<string> RemovedLanguages { get; } = [];
    public List<string> AddedLanguages { get; } = [];

    public string PrimaryLanguage => Languages.FirstOrDefault(l => l.IsPrimary)?.Code ?? Languages.FirstOrDefault()?.Code ?? "en-US";

    partial void OnFilterTextChanged(string value) => RefreshCultures();

    private void RefreshCultures() =>
        FilteredCultures = new ObservableCollection<LanguageModel>(_allCultures.Where(c => Cultures.Matches(c, FilterText)).Take(100));

    [RelayCommand]
    private void AddLanguage(LanguageModel? model)
    {
        model ??= SelectedCulture;
        if (model?.Culture != null)
        {
            AddCode(model.Culture.Name, model.Language);
            _allCultures.Remove(model);
            RefreshCultures();
        }
        else if (!string.IsNullOrWhiteSpace(FilterText))
        {
            // Custom code (e.g. "pt-BR-x-informal" or a framework-specific tag)
            AddCode(FilterText.Trim(), FilterText.Trim());
            FilterText = string.Empty;
        }
    }

    private void AddCode(string code, string displayName)
    {
        if (Languages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))) return;
        Languages.Add(new LanguageEntry { Code = code, DisplayName = displayName });
        if (!RemovedLanguages.Remove(code)) AddedLanguages.Add(code);
    }

    [RelayCommand]
    private void RemoveLanguage(LanguageEntry? entry)
    {
        if (entry == null || entry.IsPrimary || Languages.Count <= 1) return;
        Languages.Remove(entry);
        if (!AddedLanguages.Remove(entry.Code)) RemovedLanguages.Add(entry.Code);

        try
        {
            var culture = CultureInfo.GetCultureInfo(entry.Code);
            _allCultures.Add(new LanguageModel { Culture = culture, Language = culture.DisplayName });
            _allCultures.Sort((a, b) => string.Compare(a.Language, b.Language, StringComparison.CurrentCulture));
        }
        catch (CultureNotFoundException)
        {
            // Custom code — nothing to re-add to the picker.
        }
        RefreshCultures();
    }

    [RelayCommand]
    private void SetPrimary(LanguageEntry? entry)
    {
        if (entry == null) return;
        foreach (var lang in Languages) lang.IsPrimary = lang == entry;
    }

    [RelayCommand]
    private void MoveUp(LanguageEntry? entry)
    {
        if (entry == null) return;
        var index = Languages.IndexOf(entry);
        if (index > 0) Languages.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown(LanguageEntry? entry)
    {
        if (entry == null) return;
        var index = Languages.IndexOf(entry);
        if (index >= 0 && index < Languages.Count - 1) Languages.Move(index, index + 1);
    }
}

/// <summary>A language row in the Manage Languages dialog.</summary>
public partial class LanguageEntry : ObservableObject
{
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    private bool isPrimary;

    [ObservableProperty] private int translationCount;
    [ObservableProperty] private int totalKeys;

    public bool CanRemove => !IsPrimary;

    public string Summary => TotalKeys > 0
        ? string.Create(CultureInfo.CurrentCulture, $"{TranslationCount}/{TotalKeys} translated")
        : "new";
}
