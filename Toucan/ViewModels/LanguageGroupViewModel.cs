using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class LanguageGroupViewModel : ObservableObject
{
    public string Namespace { get; private set; }
    public ObservableCollection<TranslationItemViewModel> Translations { get; private set; } = [];
    private readonly System.Func<Toucan.Core.Models.TranslationItem, TranslationItemViewModel> _translationItemFactory;

    public LanguageGroupViewModel(string ns, System.Func<Toucan.Core.Models.TranslationItem, TranslationItemViewModel>? translationItemFactory = null)
    {
        Namespace = ns;
        _translationItemFactory = translationItemFactory ?? (ti => new TranslationItemViewModel(ti));
    }

    public void LoadTranslations(IEnumerable<TranslationItem> settings)
    {
        Translations.Clear();
        foreach (TranslationItem t in settings.OrderBy(o => o.Language))
        {
            Translations.Add(_translationItemFactory(t));
        }
        OnPropertyChanged(nameof(Translations));
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
