using OPEdit.Extensions;
namespace OPEdit.Core.Models;

public class SummaryInfo
{
    public double Languages { get; private set; }
    public double Translations { get; private set; }
    public List<SummaryItem> Details { get; private set; } = new List<SummaryItem>();

    public void Update(IEnumerable<LanguageSetting> settings)
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

            var translated = languageNamespaces.Count - missingLanguageCount;
            Details.Add(new SummaryItem()
            {
                Language = language,
                Potential = Translations,
                Missing = missingLanguageCount
            });
        }


    }

}
