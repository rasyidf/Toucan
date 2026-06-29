using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Hybrid search service combining exact, prefix, substring, and trigram-based fuzzy matching.
/// Registered as a singleton — holds no mutable state.
/// </summary>
public class FuzzySearchService : IFuzzySearchService
{
    private const int MaxQueryLength = 500;
    private const int MinFuzzyQueryLength = 3;
    private const double FuzzyThreshold = 0.3;

    private const double ExactScore = 1.0;
    private const double PrefixScore = 0.9;
    private const double ContainsScore = 0.7;

    /// <inheritdoc />
    public IReadOnlyList<SearchMatch> Search(IEnumerable<TranslationItem> items, string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return items
                .Select(item => new SearchMatch(item, ExactScore, SearchMatchType.Exact))
                .ToList();
        }

        var normalizedQuery = query.Length > MaxQueryLength
            ? query[..MaxQueryLength]
            : query;

        var results = new List<SearchMatch>();

        foreach (var item in items)
        {
            var match = ScoreItem(item, normalizedQuery);
            if (match is not null)
            {
                results.Add(new SearchMatch(item, match.Value.Score, match.Value.MatchType));
            }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    /// <inheritdoc />
    public double ComputeTrigramSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0.0;

        // ponytail: use int-packed trigrams to avoid string allocations entirely
        var sourceTrigrams = GetPackedTrigrams(source);
        var targetTrigrams = GetPackedTrigrams(target);

        if (sourceTrigrams.Count == 0 || targetTrigrams.Count == 0)
            return 0.0;

        int intersectionCount = 0;
        foreach (var trigram in sourceTrigrams)
        {
            if (targetTrigrams.Contains(trigram))
                intersectionCount++;
        }

        int unionCount = sourceTrigrams.Count + targetTrigrams.Count - intersectionCount;
        return unionCount == 0 ? 0.0 : (double)intersectionCount / unionCount;
    }

    /// <summary>
    /// Scores a translation item against the query using the hybrid matching algorithm.
    /// Returns the best match (highest score) across namespace and value, or null if no match.
    /// </summary>
    internal (double Score, SearchMatchType MatchType)? ScoreItem(TranslationItem item, string query)
    {
        var ns = item.Namespace ?? string.Empty;
        var value = item.Value ?? string.Empty;
        var queryLower = query.ToLowerInvariant();

        // Check exact match
        if (ns.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return (ExactScore, SearchMatchType.Exact);
        }

        // Check prefix match
        if (ns.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return (PrefixScore, SearchMatchType.Prefix);
        }

        // Check contains match
        if (ns.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return (ContainsScore, SearchMatchType.Contains);
        }

        // Check fuzzy match (only for queries >= 3 chars)
        if (queryLower.Length >= MinFuzzyQueryLength)
        {
            var nsSimilarity = ComputeTrigramSimilarity(ns, query);
            var valueSimilarity = ComputeTrigramSimilarity(value, query);
            var bestSimilarity = Math.Max(nsSimilarity, valueSimilarity);

            if (bestSimilarity > FuzzyThreshold)
            {
                return (bestSimilarity, SearchMatchType.Fuzzy);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns all 3-character substrings (trigrams) of the input string, lowercased.
    /// Kept for backward compatibility / test access.
    /// </summary>
    internal static HashSet<string> GetTrigrams(string s)
    {
        if (string.IsNullOrEmpty(s))
            return [];

        var lower = s.ToLowerInvariant();
        var set = new HashSet<string>();

        for (int i = 0; i <= lower.Length - 3; i++)
        {
            set.Add(lower.Substring(i, 3));
        }

        return set;
    }

    /// <summary>
    /// Zero-allocation trigram computation: packs 3 chars into a single long.
    /// Avoids creating thousands of 3-char strings during search.
    /// </summary>
    private static HashSet<long> GetPackedTrigrams(string s)
    {
        if (s.Length < 3) return [];

        var set = new HashSet<long>(s.Length);
        ReadOnlySpan<char> span = s.AsSpan();

        for (int i = 0; i <= span.Length - 3; i++)
        {
            long packed = ((long)char.ToLowerInvariant(span[i]) << 32)
                        | ((long)char.ToLowerInvariant(span[i + 1]) << 16)
                        | char.ToLowerInvariant(span[i + 2]);
            set.Add(packed);
        }

        return set;
    }
}
