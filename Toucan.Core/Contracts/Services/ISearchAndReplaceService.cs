using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Stateless search-and-replace over translation keys and values.
/// Supports literal and regex matching, match case, whole word, and language / key filters.
/// </summary>
public interface ISearchAndReplaceService
{
    /// <summary>Finds every match in keys and values. An invalid regex throws <see cref="ArgumentException"/>.</summary>
    IReadOnlyList<SearchResultItem> Search(IEnumerable<TranslationItem> items, string query, SearchOptions options);

    /// <summary>Fills <see cref="SearchResultItem.ReplacedValue"/> for value matches without modifying anything.</summary>
    IReadOnlyList<SearchResultItem> PreviewReplace(IReadOnlyList<SearchResultItem> results, string query, string replacement, SearchOptions options);

    /// <summary>Applies the replacement to the values of the matched items. Returns how many items changed.</summary>
    int ApplyReplace(IEnumerable<TranslationItem> allItems, IReadOnlyList<SearchResultItem> results, string query, string replacement, SearchOptions options);
}
