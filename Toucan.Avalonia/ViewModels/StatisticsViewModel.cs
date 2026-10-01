using System.Globalization;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public sealed class LanguageStatRow
{
    public string Language { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Translated { get; init; }
    public int Missing { get; init; }
    public int Total { get; init; }
    public int Approved { get; init; }
    public double Percent { get; init; }
    public string PercentText => Percent.ToString("F0", CultureInfo.CurrentCulture) + "%";

    /// <summary>"good" (≥95%), "warn" (≥70%), or "bad" — used as a style class.</summary>
    public string Health => StatisticsViewModel.HealthOf(Percent);
}

/// <summary>Project-wide and per-language translation progress.</summary>
public sealed class StatisticsViewModel
{
    public string Summary { get; }
    public double OverallPercent { get; }
    public string OverallPercentText { get; }
    public string OverallHealth { get; }
    public string TotalKeys { get; }
    public string TotalLanguages { get; }
    public string TotalApproved { get; }
    public IReadOnlyList<LanguageStatRow> Languages { get; }

    public StatisticsViewModel(IEnumerable<TranslationItem> translations)
    {
        var items = translations?.Where(t => !string.IsNullOrWhiteSpace(t.Namespace)).ToList() ?? [];
        var total = items.Count;
        var translated = items.Count(t => !string.IsNullOrWhiteSpace(t.Value));
        var approved = items.Count(t => t.IsApproved);
        var groups = items.GroupBy(t => t.Language)
            .OrderByDescending(g => g.Count(t => !string.IsNullOrWhiteSpace(t.Value)))
            .ToList();

        var culture = CultureInfo.CurrentCulture;
        OverallPercent = total == 0 ? 0 : (double)translated / total * 100;
        OverallPercentText = OverallPercent.ToString("F0", culture) + "%";
        OverallHealth = HealthOf(OverallPercent);
        Summary = string.Create(culture, $"{translated:N0} of {total:N0} translations completed");
        TotalKeys = items.Select(t => t.Namespace).Distinct().Count().ToString("N0", culture);
        TotalLanguages = groups.Count.ToString(culture);
        TotalApproved = approved.ToString("N0", culture);

        Languages = groups.Select(g =>
        {
            var langTotal = g.Count();
            var langTranslated = g.Count(t => !string.IsNullOrWhiteSpace(t.Value));
            var display = DisplayNameOf(g.Key);
            return new LanguageStatRow
            {
                Language = g.Key,
                DisplayName = display != g.Key ? display : string.Empty,
                Translated = langTranslated,
                Missing = langTotal - langTranslated,
                Total = langTotal,
                Approved = g.Count(t => t.IsApproved),
                Percent = langTotal == 0 ? 0 : (double)langTranslated / langTotal * 100
            };
        }).ToList();
    }

    internal static string HealthOf(double percent) => percent switch
    {
        >= 95 => "good",
        >= 70 => "warn",
        _ => "bad"
    };

    internal static string DisplayNameOf(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (CultureNotFoundException) { return code; }
    }
}
