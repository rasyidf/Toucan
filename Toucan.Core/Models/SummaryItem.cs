using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Core.Models;

public partial class SummaryItem : ObservableObject
{
    [ObservableProperty]
    private bool isExpanded;

    public string Language { get; set; }

    public double Percentage => Math.Round(Math.Floor((Potential - Missing) / Potential * 100), 2);

    public double Missing { get; set; }

    public DateTime Updated { get; } = DateTime.Now;

    public double Potential { get; set; }

    public string Stats => Missing > 0 ? $"{Missing}/{Potential}" : $"OK!";
}
