using System.Collections.Generic;

namespace Toucan.Core.Models;

/// <summary>
/// Describes a translation provider's identity and required configuration fields.
/// Used by the Provider Settings UI to show the correct option/secret fields.
/// </summary>
public class ProviderDefinition
{
    /// <summary>Provider name matching ITranslationProvider.Name (e.g. "Google", "DeepL").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Short description for the UI.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>True for app-shipped providers, false for user-added custom ones.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// Options fields the provider recognizes (non-secret configuration).
    /// Key = field name, Value = placeholder/description shown in the UI.
    /// </summary>
    public Dictionary<string, string> OptionFields { get; init; } = [];

    /// <summary>
    /// Secrets fields the provider recognizes (API keys, tokens — stored encrypted).
    /// Key = field name, Value = placeholder/description shown in the UI.
    /// </summary>
    public Dictionary<string, string> SecretFields { get; init; } = [];

    /// <summary>Default values for option fields. Used when creating fresh provider settings.</summary>
    public Dictionary<string, string> DefaultValues { get; init; } = [];
}
