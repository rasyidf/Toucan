namespace Toucan.Core.Models;

public class PretranslationJob
{
    /// <summary>
    /// The namespace/key for the item being translated
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Source text to use for translation
    /// </summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>
    /// Source language code (e.g. en-US)
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Target language code (e.g. fr-FR)
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;
}
