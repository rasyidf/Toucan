using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Toucan.Core.Models;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class StatisticsDialog : FluentWindow
{
    public StatisticsDialog(IEnumerable<TranslationItem> translations, Window? owner = null)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;
        DataContext = new StatisticsViewModel(translations);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

internal sealed class LanguageStatRow
{
    public string Language { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int Translated { get; init; }
    public int Missing { get; init; }
    public int Total { get; init; }
    public int Approved { get; init; }
    public double Percent { get; init; }
    public Brush StatusColor => Percent switch
    {
        >= 95 => new SolidColorBrush(Color.FromRgb(63, 185, 80)),   // green
        >= 70 => new SolidColorBrush(Color.FromRgb(210, 169, 34)),  // yellow
        _ => new SolidColorBrush(Color.FromRgb(248, 81, 73))        // red
    };
}

internal sealed class StatisticsViewModel
{
    public string Summary { get; }
    public double OverallPercent { get; }
    public string OverallPercentText { get; }
    public string TotalKeys { get; }
    public string TotalLanguages { get; }
    public string TotalApproved { get; }
    public Brush OverallColor { get; }
    public List<LanguageStatRow> Languages { get; }

    public StatisticsViewModel(IEnumerable<TranslationItem> translations)
    {
        var items = translations?.ToList() ?? [];
        var total = items.Count;
        var translated = items.Count(t => !string.IsNullOrWhiteSpace(t.Value));
        var approved = items.Count(t => t.IsApproved);
        var languages = items.GroupBy(t => t.Language).OrderByDescending(g => g.Count(t => !string.IsNullOrWhiteSpace(t.Value))).ToList();

        var uniqueKeys = items.Select(t => t.Namespace).Distinct().Count();
        OverallPercent = total == 0 ? 0 : (double)translated / total * 100;
        OverallPercentText = $"{OverallPercent:F0}%";
        Summary = $"{translated:N0} of {total:N0} translations completed";
        TotalKeys = uniqueKeys.ToString("N0");
        TotalLanguages = languages.Count.ToString();
        TotalApproved = approved.ToString("N0");
        OverallColor = OverallPercent switch
        {
            >= 95 => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            >= 70 => new SolidColorBrush(Color.FromRgb(210, 169, 34)),
            _ => new SolidColorBrush(Color.FromRgb(248, 81, 73))
        };

        Languages = languages.Select(g =>
        {
            var langTotal = g.Count();
            var langTranslated = g.Count(t => !string.IsNullOrWhiteSpace(t.Value));
            var langApproved = g.Count(t => t.IsApproved);
            var displayName = GetLanguageDisplayName(g.Key);
            return new LanguageStatRow
            {
                Language = g.Key,
                DisplayName = displayName != g.Key ? $"({displayName})" : "",
                Translated = langTranslated,
                Missing = langTotal - langTranslated,
                Total = langTotal,
                Approved = langApproved,
                Percent = langTotal == 0 ? 0 : (double)langTranslated / langTotal * 100
            };
        }).ToList();
    }

    private static string GetLanguageDisplayName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            return culture.DisplayName;
        }
        catch { return code; }
    }
}
