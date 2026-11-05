namespace Toucan.Core.Models;

public sealed class GlossaryEntry
{
    public string Term { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Forbidden { get; set; }
    public Dictionary<string, string> Translations { get; set; } = [];
}
