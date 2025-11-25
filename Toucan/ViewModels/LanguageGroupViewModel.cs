using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public class LanguageGroupViewModel : ObservableObject
{
    public string Namespace { get; private set; }

    public ObservableCollection<TranslationItemViewModel> Translations { get; private set; } = new();

    public LanguageGroupViewModel(string ns)
    {
        Namespace = ns;
    }

    public void LoadTranslations(IEnumerable<TranslationItem> settings)
    {
        Translations.Clear();
        foreach (var t in settings.OrderBy(o => o.Language))
        {
            Translations.Add(new TranslationItemViewModel(t));
        }
        OnPropertyChanged(nameof(Translations));
    }
}
