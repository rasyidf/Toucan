using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

/// <summary>Statistics panel: translation progress counts shown in the popover.</summary>
internal partial class StatusBarViewModel
{
    [ObservableProperty]
    private int totalKeys;

    [ObservableProperty]
    private int translatedKeys;

    [ObservableProperty]
    private int errors;

    [ObservableProperty]
    private int warnings;

    /// <summary>Per-language breakdown shown in the statistics popover card.</summary>
    public ObservableCollection<SummaryItem> LanguageStats { get; } = [];

    public string TranslationProgress => TotalKeys == 0
        ? "No keys"
        : $"{TranslatedKeys}/{TotalKeys}";

    public void UpdateStatistics(int total, int translated, int errorCount, int warningCount, IEnumerable<SummaryItem>? perLanguage = null)
    {
        TotalKeys = total;
        TranslatedKeys = translated;
        Errors = errorCount;
        Warnings = warningCount;
        OnPropertyChanged(nameof(TranslationProgress));

        LanguageStats.Clear();
        if (perLanguage is not null)
        {
            foreach (var item in perLanguage)
                LanguageStats.Add(item);
        }
    }
}
