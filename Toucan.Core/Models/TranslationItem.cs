namespace Toucan.Core.Models;

public class TranslationItem
{
    public required string Language { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
}
