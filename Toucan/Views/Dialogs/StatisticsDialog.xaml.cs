using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Toucan.Core.Models;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class StatisticsDialog : FluentWindow
{
    public StatisticsDialog(IEnumerable<TranslationItem> translations, Window owner = null)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;
        DataContext = new StatisticsViewModel(translations);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

internal class LanguageStatRow
{
    public string Language { get; init; } = "";
    public int Translated { get; init; }
    public int Missing { get; init; }
    public int Total { get; init; }
    public double Percent { get; init; }
}

internal class StatisticsViewModel
{
    public string Title { get; }
    public string Summary { get; }
    public double OverallPercent { get; }
    public List<LanguageStatRow> Languages { get; }

    public StatisticsViewModel(IEnumerable<TranslationItem> translations)
    {
        var items = translations?.ToList() ?? [];
        var total = items.Count;
        var translated = items.Count(t => !string.IsNullOrWhiteSpace(t.Value));
        var languages = items.GroupBy(t => t.Language).OrderBy(g => g.Key).ToList();

        Title = "Translation Statistics";
        Summary = $"{translated} of {total} translations completed across {languages.Count} languages";
        OverallPercent = total == 0 ? 0 : (double)translated / total * 100;

        Languages = languages.Select(g =>
        {
            var langTotal = g.Count();
            var langTranslated = g.Count(t => !string.IsNullOrWhiteSpace(t.Value));
            return new LanguageStatRow
            {
                Language = g.Key,
                Translated = langTranslated,
                Missing = langTotal - langTranslated,
                Total = langTotal,
                Percent = langTotal == 0 ? 0 : (double)langTranslated / langTotal * 100
            };
        }).ToList();
    }
}
