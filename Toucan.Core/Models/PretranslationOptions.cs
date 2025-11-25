namespace Toucan.Core.Models;

public class PretranslationOptions
{
    /// <summary>
    /// If true, overwrite existing translations.
    /// </summary>
    public bool Overwrite { get; set; } = false;

    /// <summary>
    /// Optional per-provider options bag.
    /// </summary>
    public IDictionary<string, string>? ProviderOptions { get; set; }
}
