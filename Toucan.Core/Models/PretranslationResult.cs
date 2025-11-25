namespace Toucan.Core.Models;

public class PretranslationResult
{
    public List<PretranslationItemResult> Items { get; } = new();

    public bool Success => Items.All(i => i.Succeeded);

    public string Summary => $"{Items.Count(i => i.Succeeded)} succeeded, {Items.Count(i => !i.Succeeded)} failed";
}

public class PretranslationItemResult
{
    public string Namespace { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? TranslatedValue { get; set; }
    public string? Provider { get; set; }
    public string? ErrorMessage { get; set; }
}
