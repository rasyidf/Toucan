using Toucan.Extensions;
using System.Collections.ObjectModel;

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
        var allNamespace = settings.ToNamespaces().ToList();
        var allLanguages = settings.ToLanguages().ToList();

        Languages = allLanguages.Count;
        Translations = allNamespace.Count;

        Details.Clear();
        foreach (var language in allLanguages)
        {
            var languageNamespaces = settings.ToNamespaces(language).ToList();
            double missingLanguageCount = allNamespace.Except(languageNamespaces).Count();

            Details.Add(new SummaryItem
            {
                IsExpanded = false,
                Language = language,
                Potential = Translations,
                Missing = missingLanguageCount
            });
        }
    }
}
