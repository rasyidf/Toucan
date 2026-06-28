using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Core.Models;

public partial class SummaryItem : ObservableObject
{
    [ObservableProperty]
    private bool isExpanded;

    public string Language { get; set; } = string.Empty;

    public double Percentage => Potential == 0 ? 100 : Math.Round(Math.Floor((Potential - Missing) / Potential * 100), 2);

    public double Missing { get; set; }

    public DateTime Updated { get; } = DateTime.Now;

    public double Potential { get; set; }

    /// <summary>Number of keys with a non-empty translation value.</summary>
    public int Translated { get; set; }

    /// <summary>Number of keys with an empty translation value (not yet translated).</summary>
    public int Empty { get; set; }

    /// <summary>Number of keys marked as approved/reviewed.</summary>
    public int Approved { get; set; }

    /// <summary>Number of keys that are translated but not yet approved (needs review).</summary>
    public int NeedsReview { get; set; }

    public string Stats => Missing > 0 ? $"{Missing}/{Potential}" : $"OK!";

    /// <summary>Compact progress indicator like i18n Ally: "4/9" translated.</summary>
    public string TranslatedStats => $"({Translated}/{(int)Potential})";
}
