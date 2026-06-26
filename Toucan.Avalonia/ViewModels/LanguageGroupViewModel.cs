using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public class LanguageGroupViewModel : ObservableObject
{
    public string Namespace { get; }
    public ObservableCollection<TranslationItemViewModel> Translations { get; } = new();

    public LanguageGroupViewModel(string ns) => Namespace = ns;

    public void LoadTranslations(IEnumerable<TranslationItem> items)
    {
        Translations.Clear();
        foreach (var t in items.OrderBy(o => o.Language))
            Translations.Add(new TranslationItemViewModel(t));
        OnPropertyChanged(nameof(Translations));
    }
}
