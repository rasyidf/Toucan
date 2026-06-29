namespace Toucan.Core.Models;

/// <summary>
/// Stores the last-saved state of a translation item for dirty-tracking purposes.
/// Keyed by composite (Language, Namespace).
/// </summary>
internal class TranslationBaseline
{
    /// <summary>Language code component of the composite key.</summary>
    public required string Language { get; init; }

    /// <summary>Namespace component of the composite key.</summary>
    public required string Namespace { get; init; }

    /// <summary>Value at last save/load.</summary>
    public string SavedValue { get; set; } = string.Empty;

    /// <summary>Comment at last save/load.</summary>
    public string SavedComment { get; set; } = string.Empty;
}
