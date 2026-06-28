using Toucan.Extensions;
using System.Collections.ObjectModel;
using System.Linq;

namespace Toucan.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class SummaryInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<SummaryItem> details = [];

    public double Languages { get; private set; }
    public double Translations { get; private set; }


    [ObservableProperty]
    private bool _expandState = true;

    [RelayCommand]
    private void ToggleExpandAll()
    {
        foreach (var item in Details)
            item.IsExpanded = ExpandState;

        ExpandState = !ExpandState;
    }

    public void Update(IEnumerable<TranslationItem> settings)
    {
        // Only consider items with real namespaces for language listing
        var parsableSettings = settings.ForParse().ToList();
        var allNamespace = parsableSettings.ToNamespaces().ToList();
        var allLanguages = parsableSettings.ToLanguages().ToList();

        Languages = allLanguages.Count;
        Translations = allNamespace.Count;

        Details.Clear();
        foreach (var language in allLanguages)
        {
            var languageItems = parsableSettings.Where(t => t.Language == language).ToList();
            var languageNamespaces = parsableSettings.ToNamespaces(language).ToList();
            double missingLanguageCount = allNamespace.Except(languageNamespaces).Count();

            int translated = languageItems.Count(t => !string.IsNullOrEmpty(t.Value));
            int empty = (int)Translations - translated;
            int approved = languageItems.Count(t => t.IsApproved);
            int needsReview = translated - approved;

            Details.Add(new SummaryItem
            {
                IsExpanded = false,
                Language = language,
                Potential = Translations,
                Missing = missingLanguageCount,
                Translated = translated,
                Empty = empty,
                Approved = approved,
                NeedsReview = needsReview < 0 ? 0 : needsReview
            });
        }
    }
}
