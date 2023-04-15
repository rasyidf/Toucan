using CommunityToolkit.Mvvm.ComponentModel;
using OPEdit.Extensions;
using System.Collections.ObjectModel;

namespace OPEdit.Core.Models;

public partial class SummaryInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<SummaryItem> details = new ObservableCollection<SummaryItem>();

    public double Languages { get; private set; }
    public double Translations { get; private set; }

    public void Update(IEnumerable<TranslationItem> settings)
    {

        var allNamespace = settings.ToNamespaces().ToList();
        var allLanguages = settings.ToLanguages().ToList();

        Languages = allLanguages.Count;
        Translations = allNamespace.Count;

        Details.Clear();
        foreach (var language in allLanguages)
        {
            var languageNamespaces = settings.ToNamespaces(language).ToList();
            double missingLanguageCount = allNamespace.Except(languageNamespaces).Count();
            
            Details.Add(new SummaryItem()
            {
                IsExpanded = false,
                Language = language,
                Potential = Translations,
                Missing = missingLanguageCount
            });
        }


    }

}
