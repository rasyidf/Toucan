namespace OPEdit.Core.Models;

public class SummaryItem
{
    public string Language { get; set; }
    public double Percentage { get { return Math.Round(Math.Floor((Potential - Missing) / Potential * 100), 2); } }
    public double Missing { get; set; }
    public DateTime Updated { get; } = DateTime.Now;
    public double Potential { get; set; }

    public string Stats => Missing > 0 ? $"(missing {Missing} of {Potential})"
        : $"OK!";

}
