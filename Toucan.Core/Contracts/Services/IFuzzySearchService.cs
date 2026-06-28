using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface IFuzzySearchService
{
    /// <summary>
    /// Searches translation items using hybrid fuzzy matching against both namespace and value.
    /// </summary>
    /// <param name="items">All translation items to search within.</param>
    /// <param name="query">The search query string. Returns all items if null or empty.</param>
    /// <returns>Ranked list of search matches ordered by score descending.</returns>
    IReadOnlyList<SearchMatch> Search(IEnumerable<TranslationItem> items, string? query);

    /// <summary>
    /// Computes the trigram Jaccard similarity between two strings.
    /// </summary>
    double ComputeTrigramSimilarity(string source, string target);
}
