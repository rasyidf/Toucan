namespace Toucan.Core.Models;

/// <summary>
/// Determines the scope of a search-and-replace operation.
/// </summary>
public enum SearchScope
{
    /// <summary>Search across all languages and namespaces.</summary>
    AllLanguages,

    /// <summary>Search only within a specific language (filter = language code).</summary>
    SpecificLanguage,

    /// <summary>Search only within the current namespace prefix (filter = namespace).</summary>
    CurrentNamespace
}
