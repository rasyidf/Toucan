using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Stateless search-and-replace across translation item keys and values.
/// Supports literal substring and regex modes with scope filtering.
/// </summary>
public class SearchAndReplaceService : ISearchAndReplaceService
{
    /// <inheritdoc />
    public IReadOnlyList<SearchResultItem> Search(
        IEnumerable<TranslationItem> items,
        string query,
        bool useRegex,
        SearchScope scope,
        string? scopeFilter = null)
    {
        if (string.IsNullOrEmpty(query))
            return [];

        var filtered = ApplyScope(items, scope, scopeFilter);
        var results = new List<SearchResultItem>();

        if (useRegex)
        {
            Regex? regex;
            try { regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException) { return []; } // ponytail: invalid regex → empty results, no crash

            foreach (var item in filtered)
            {
                CollectRegexMatches(item, regex, results);
            }
        }
        else
        {
            foreach (var item in filtered)
            {
                CollectLiteralMatches(item, query, results);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchResultItem> PreviewReplace(
        IReadOnlyList<SearchResultItem> results,
        string replacement,
        bool useRegex)
    {
        // ponytail: preview is computed per-item without modifying anything
        foreach (var result in results)
        {
            if (useRegex)
            {
                try
                {
                    var regex = new Regex(
                        Regex.Escape(result.MatchText).Replace(@"\*", ".*"),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1));
                    result.ReplacedValue = result.Value[..result.MatchStart]
                        + replacement
                        + result.Value[(result.MatchStart + result.MatchLength)..];
                }
                catch
                {
                    result.ReplacedValue = result.Value[..result.MatchStart]
                        + replacement
                        + result.Value[(result.MatchStart + result.MatchLength)..];
                }
            }
            else
            {
                result.ReplacedValue = result.Value[..result.MatchStart]
                    + replacement
                    + result.Value[(result.MatchStart + result.MatchLength)..];
            }
        }

        return results;
    }

    /// <inheritdoc />
    public int ApplyReplace(
        IEnumerable<TranslationItem> allItems,
        IReadOnlyList<SearchResultItem> results,
        string query,
        string replacement,
        bool useRegex)
    {
        // Build a lookup: (namespace, language) → TranslationItem for O(1) access
        var lookup = allItems.ToDictionary(
            i => (i.Namespace, i.Language),
            i => i,
            EqualityComparer<(string, string)>.Default);

        int modified = 0;

        // Group results by (key, language) to apply all replacements per item in reverse order
        var grouped = results
            .GroupBy(r => (r.Key, r.Language))
            .ToList();

        foreach (var group in grouped)
        {
            if (!lookup.TryGetValue((group.Key.Key, group.Key.Language), out var item))
                continue;

            if (useRegex)
            {
                try
                {
                    var regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                    var newValue = regex.Replace(item.Value, replacement);
                    if (newValue != item.Value)
                    {
                        item.Value = newValue;
                        modified++;
                    }
                }
                catch { /* invalid regex at apply time — skip */ }
            }
            else
            {
                var newValue = item.Value.Replace(query, replacement, StringComparison.OrdinalIgnoreCase);
                if (newValue != item.Value)
                {
                    item.Value = newValue;
                    modified++;
                }
            }
        }

        return modified;
    }

    private static IEnumerable<TranslationItem> ApplyScope(
        IEnumerable<TranslationItem> items, SearchScope scope, string? filter)
    {
        return scope switch
        {
            SearchScope.SpecificLanguage when !string.IsNullOrEmpty(filter) =>
                items.Where(i => i.Language.Equals(filter, StringComparison.OrdinalIgnoreCase)),
            SearchScope.CurrentNamespace when !string.IsNullOrEmpty(filter) =>
                items.Where(i => i.Namespace.StartsWith(filter, StringComparison.OrdinalIgnoreCase)),
            _ => items
        };
    }

    private static void CollectLiteralMatches(TranslationItem item, string query, List<SearchResultItem> results)
    {
        // Search in namespace (key)
        int idx = 0;
        while ((idx = item.Namespace.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            results.Add(new SearchResultItem
            {
                Key = item.Namespace,
                Language = item.Language,
                Value = item.Namespace,
                MatchStart = idx,
                MatchLength = query.Length
            });
            idx += query.Length;
        }

        // Search in value
        idx = 0;
        while ((idx = item.Value.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            results.Add(new SearchResultItem
            {
                Key = item.Namespace,
                Language = item.Language,
                Value = item.Value,
                MatchStart = idx,
                MatchLength = query.Length
            });
            idx += query.Length;
        }
    }

    private static void CollectRegexMatches(TranslationItem item, Regex regex, List<SearchResultItem> results)
    {
        // Search in namespace (key)
        foreach (Match m in regex.Matches(item.Namespace))
        {
            results.Add(new SearchResultItem
            {
                Key = item.Namespace,
                Language = item.Language,
                Value = item.Namespace,
                MatchStart = m.Index,
                MatchLength = m.Length
            });
        }

        // Search in value
        foreach (Match m in regex.Matches(item.Value))
        {
            results.Add(new SearchResultItem
            {
                Key = item.Namespace,
                Language = item.Language,
                Value = item.Value,
                MatchStart = m.Index,
                MatchLength = m.Length
            });
        }
    }
}
