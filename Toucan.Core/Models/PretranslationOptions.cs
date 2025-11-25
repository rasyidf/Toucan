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

    /// <summary>
    /// If true the pretranslation run is a preview / dry-run and translations should not be applied to target items.
    /// </summary>
    public bool PreviewOnly { get; set; } = false;
}
