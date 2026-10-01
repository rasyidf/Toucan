using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// One card in the translation editor: a key (namespace) with its value in every language.
/// Plural keys (key_one, key_other, ...) are merged into a single card with one section per category.
/// </summary>
public partial class LanguageGroupViewModel : ObservableObject, IDisposable
{
    private readonly Func<TranslationItem, TranslationItemViewModel> _translationItemFactory;

    public LanguageGroupViewModel(string ns, Func<TranslationItem, TranslationItemViewModel>? translationItemFactory = null)
    {
        Namespace = ns;
        _translationItemFactory = translationItemFactory ?? (ti => new TranslationItemViewModel(ti));
    }

    public string Namespace { get; }

    /// <summary>Last segment of the key, shown prominently on the card.</summary>
    public string ShortName => Namespace.Contains('.', StringComparison.Ordinal) ? Namespace[(Namespace.LastIndexOf('.') + 1)..] : Namespace;

    /// <summary>Parent path of the key (everything before the last dot), shown dimmed.</summary>
    public string ParentPath => Namespace.Contains('.', StringComparison.Ordinal) ? Namespace[..Namespace.LastIndexOf('.')] : string.Empty;

    /// <summary>Parent path including the trailing dot, for inline display before <see cref="ShortName"/>.</summary>
    public string ParentPrefix => ParentPath.Length > 0 ? ParentPath + "." : string.Empty;

    public ObservableCollection<TranslationItemViewModel> Translations { get; } = [];

    public ObservableCollection<PluralVariantGroup> PluralVariants { get; } = [];

    public bool IsPluralGroup => PluralVariants.Count > 0;

    /// <summary>All item view models on this card, including plural variants.</summary>
    public IEnumerable<TranslationItemViewModel> AllItems => Translations.Concat(PluralVariants.SelectMany(v => v.Translations));

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool isSelectedForBulk;

    /// <summary>Raised when the card's "translate" action is clicked.</summary>
    public event EventHandler? TranslateRequested;

    public void LoadTranslations(IEnumerable<TranslationItem> items)
    {
        DisposeItems();
        Translations.Clear();
        foreach (var t in OrderByPrimary(items))
            Translations.Add(_translationItemFactory(t));
    }

    public void LoadPluralVariants(IEnumerable<IGrouping<string, TranslationItem>> variantGroups)
    {
        DisposeItems();
        PluralVariants.Clear();
        foreach (var group in variantGroups)
        {
            var variant = new PluralVariantGroup
            {
                Category = PluralService.GetCategory(group.Key) ?? group.Key,
                FullNamespace = group.Key
            };
            foreach (var t in OrderByPrimary(group))
                variant.Translations.Add(_translationItemFactory(t));
            PluralVariants.Add(variant);
        }
        OnPropertyChanged(nameof(IsPluralGroup));
    }

    private static IEnumerable<TranslationItem> OrderByPrimary(IEnumerable<TranslationItem> items)
    {
        var primary = StatusBarService.Instance.ViewModel?.DefaultLanguage;
        return string.IsNullOrEmpty(primary)
            ? items.OrderBy(o => o.Language, StringComparer.Ordinal)
            : items.OrderBy(o => string.Equals(o.Language, primary, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                   .ThenBy(o => o.Language, StringComparer.Ordinal);
    }

    [RelayCommand]
    private void TranslateKey() => TranslateRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private Task CopyNamespace() => PlatformService.SetClipboardTextAsync(Namespace);

    public void FlushEdits()
    {
        foreach (var item in AllItems) item.Flush();
    }

    private void DisposeItems()
    {
        foreach (var item in AllItems) item.Dispose();
    }

    public void Dispose()
    {
        DisposeItems();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A single plural variant (e.g. "one") with its translations across languages.</summary>
public class PluralVariantGroup
{
    public string Category { get; set; } = string.Empty;
    public string FullNamespace { get; set; } = string.Empty;
    public ObservableCollection<TranslationItemViewModel> Translations { get; } = [];
}
