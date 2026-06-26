namespace Toucan.Core.Models;

public class PretranslationRequest
{
    public string? Language { get; set; }
    public string? Key { get; set; }
    public string? Namespace { get; set; }
    public string? Provider { get; set; }
    public IEnumerable<TranslationItem>? Items { get; set; }
    public IEnumerable<TranslationItem>? ContextItems { get; set; }
    public PretranslationOptions? Options { get; set; }
}
