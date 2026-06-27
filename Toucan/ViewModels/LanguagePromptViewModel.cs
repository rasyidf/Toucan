using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Toucan.Core;

namespace Toucan.ViewModels;



public partial class LanguagePromptViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<LanguageModel> cultureList;



    public LanguagePromptViewModel(IEnumerable<Core.Models.TranslationItem>? existingTranslations = null)
    {
        // Build a list of languages excluding any already present in the project (if provided)
        HashSet<string> existing = new(StringComparer.InvariantCultureIgnoreCase);
        if (existingTranslations != null)
        {
            foreach (var t in existingTranslations)
            {
                _ = existing.Add(t.Language);
            }
        }

        CultureList = new(
            CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .OrderBy(c => c.DisplayName)
            .Select(c => new LanguageModel { Culture = c, Language = c.DisplayName })
            .Where(l => !existing.Contains(l.Culture?.Name ?? l.Language))
            .ToList()
           );

        // Prepare a view for filtering suggestions
        FilteredView = System.Windows.Data.CollectionViewSource.GetDefaultView(CultureList);
        FilteredView.Filter = o => FilterPredicate(o as LanguageModel);
    }

    public System.ComponentModel.ICollectionView FilteredView { get; private set; }

    private bool FilterPredicate(LanguageModel? m)
    {
        if (m == null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(FilterText) || m.Language.Contains(FilterText, StringComparison.InvariantCultureIgnoreCase) ||
               (m.Culture?.NativeName)?.Contains(FilterText, StringComparison.InvariantCultureIgnoreCase) == true ||
               (m.Culture?.Name)?.Contains(FilterText, StringComparison.InvariantCultureIgnoreCase) == true;
    }

    [ObservableProperty]
    private string filterText = string.Empty;

    partial void OnFilterTextChanged(string value)
    {
        FilteredView?.Refresh();
        if (string.IsNullOrWhiteSpace(value))
        {
            SelectedLanguage = null;
            return;
        }
        var found = CultureList?.FirstOrDefault(l => string.Equals(l.Culture?.Name ?? string.Empty, value, StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(l.Language ?? string.Empty, value, StringComparison.InvariantCultureIgnoreCase) ||
            string.Equals(l.Culture?.NativeName ?? string.Empty, value, StringComparison.InvariantCultureIgnoreCase));
        SelectedLanguage = found;
    }

    // Selected language (model) after user picks a suggestion
    [ObservableProperty]
    private LanguageModel? selectedLanguage;

}
