using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.ViewModels;

public partial class LanguageGroupViewModel : ObservableObject
{
    public string Namespace { get; private set; }
    public ObservableCollection<TranslationItemViewModel> Translations { get; private set; } = [];
    private readonly System.Func<TranslationItem, TranslationItemViewModel> _translationItemFactory;

    /// <summary>
    /// When this group represents merged plural variants, each variant is stored here.
    /// Key = plural category (e.g. "one", "other"), Value = translations for that variant.
    /// </summary>
    public ObservableCollection<PluralVariantGroup> PluralVariants { get; } = [];

    /// <summary>True when this card groups plural variants under a single base key.</summary>
    public bool IsPluralGroup => PluralVariants.Count > 0;

    public LanguageGroupViewModel(string ns, System.Func<TranslationItem, TranslationItemViewModel>? translationItemFactory = null)
    {
        Namespace = ns;
        _translationItemFactory = translationItemFactory ?? (ti => new TranslationItemViewModel(ti));
    }

    public void LoadTranslations(IEnumerable<TranslationItem> settings)
    {
        foreach (var item in Translations)
        {
            item.Dispose();
        }
        Translations.Clear();
        foreach (TranslationItem t in settings.OrderBy(o => o.Language))
        {
            Translations.Add(_translationItemFactory(t));
        }
        OnPropertyChanged(nameof(Translations));
    }

    /// <summary>
    /// Loads plural variants into the group. Each variant gets its own sub-section.
    /// </summary>
    public void LoadPluralVariants(IEnumerable<IGrouping<string, TranslationItem>> variantGroups)
    {
        foreach (var variant in PluralVariants)
        {
            foreach (var item in variant.Translations)
            {
                item.Dispose();
            }
        }
        PluralVariants.Clear();

        foreach (var group in variantGroups)
        {
            var category = PluralService.GetCategory(group.Key) ?? group.Key;
            var variant = new PluralVariantGroup
            {
                Category = category,
                FullNamespace = group.Key
            };
            foreach (var t in group.OrderBy(o => o.Language))
            {
                variant.Translations.Add(_translationItemFactory(t));
            }
            PluralVariants.Add(variant);
        }

        OnPropertyChanged(nameof(PluralVariants));
        OnPropertyChanged(nameof(IsPluralGroup));
    }

    [RelayCommand]
    private void TranslateKey()
    {
        // ponytail: copies namespace to clipboard as a quick "translate key" action for now
        // upgrade path: wire to IPretranslationService for single-key translation
        if (!string.IsNullOrEmpty(Namespace))
        {
            Clipboard.SetText(Namespace);
        }
    }

    [RelayCommand]
    private void CopyNamespace()
    {
        if (!string.IsNullOrEmpty(Namespace))
        {
            Clipboard.SetText(Namespace);
        }
    }
}

/// <summary>A single plural variant (e.g. "_one") with its translations across languages.</summary>
public class PluralVariantGroup
{
    public string Category { get; set; } = string.Empty;
    public string FullNamespace { get; set; } = string.Empty;
    public ObservableCollection<TranslationItemViewModel> Translations { get; } = [];
}
