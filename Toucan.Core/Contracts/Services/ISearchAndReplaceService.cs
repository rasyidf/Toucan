using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Stateless search-and-replace service that operates on translation items.
/// Supports literal and regex modes with scope filtering.
/// </summary>
public interface ISearchAndReplaceService
{
    /// <summary>
    /// Searches all translation items for matches against the query.
    /// </summary>
    IReadOnlyList<SearchResultItem> Search(
        IEnumerable<TranslationItem> items,
        string query,
        bool useRegex,
        SearchScope scope,
        string? scopeFilter = null);

    /// <summary>
    /// Returns a preview of replacements without modifying items.
    /// </summary>
    IReadOnlyList<SearchResultItem> PreviewReplace(
        IReadOnlyList<SearchResultItem> results,
        string replacement,
        bool useRegex);

    /// <summary>
    /// Applies replacements to the underlying TranslationItems.
    /// Returns the count of items modified.
    /// </summary>
    int ApplyReplace(
        IEnumerable<TranslationItem> allItems,
        IReadOnlyList<SearchResultItem> results,
        string query,
        string replacement,
        bool useRegex);
}
