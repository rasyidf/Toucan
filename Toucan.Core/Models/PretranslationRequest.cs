namespace Toucan.Core.Models;

public class PretranslationRequest
{
    /// <summary>
    /// If provided, the target language to translate into (e.g. "fr-FR"). If null, items should already contain language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Single key (exact namespace) to pre-translate.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Namespace or prefix to pre-translate.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Optional provider name to use (DeepL, Google, Mock, etc.). If empty, the default provider selection must be used.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Items to pre-translate (optional). If provided it takes precedence over Key/Namespace filtering.
    /// </summary>
    public IEnumerable<TranslationItem>? Items { get; set; }

    /// <summary>
    /// Optional larger context (project-wide) items that can be used as source texts when resolving a translation's source value.
    /// If set, providers and services may search this collection for source texts by namespace.
    /// </summary>
    public IEnumerable<TranslationItem>? ContextItems { get; set; }

    /// <summary>
    /// Additional options controlling translation behavior.
    /// </summary>
    public PretranslationOptions? Options { get; set; }
}
