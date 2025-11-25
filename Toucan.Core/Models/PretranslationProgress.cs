namespace Toucan.Core.Models;

public class PretranslationProgress
{
    /// <summary>
    /// How many jobs are complete
    /// </summary>
    public int Completed { get; set; }

    /// <summary>
    /// Total jobs planned
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Optional human-friendly message (current provider, error, etc.)
    /// </summary>
    public string? Message { get; set; }
}
