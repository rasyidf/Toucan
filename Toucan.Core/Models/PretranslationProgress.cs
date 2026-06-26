namespace Toucan.Core.Models;

public class PretranslationProgress
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public string? Message { get; set; }
}
