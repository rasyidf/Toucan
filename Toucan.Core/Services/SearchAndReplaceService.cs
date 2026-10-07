using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Search-and-replace across translation keys and values. Every mode compiles to one <see cref="Regex"/>
/// (literal queries are escaped), so match case, whole word and replacement behave the same in all of them.
/// </summary>
public class SearchAndReplaceService : ISearchAndReplaceService
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public IReadOnlyList<SearchResultItem> Search(IEnumerable<TranslationItem> items, string query, SearchOptions options)
    {
        if (string.IsNullOrEmpty(query)) return [];
        var regex = Build(query, options); // throws ArgumentException for a bad pattern
        var results = new List<SearchResultItem>();

        var keysReported = new HashSet<string>(StringComparer.Ordinal); // a key exists once, not once per language
        foreach (var item in Filter(items, options))
        {
            if (keysReported.Add(item.Namespace)) Collect(regex, item.Namespace, item, inKey: true, results);
            Collect(regex, item.Value, item, inKey: false, results);
        }
        return results;
    }

    /// <inheritdoc />
    public IReadOnlyList<SearchResultItem> PreviewReplace(IReadOnlyList<SearchResultItem> results, string query, string replacement, SearchOptions options)
    {
        var regex = Build(query, options);
        // One preview per (key, language): all matches in a value are replaced together, exactly as Apply does.
        foreach (var group in results.Where(r => !r.InKey).GroupBy(r => (r.Key, r.Language)))
        {
            var replaced = Replace(regex, group.First().Value, replacement, options);
            foreach (var r in group)
            {
                r.ReplacedValue = replaced;
                var m = regex.Match(r.Value, r.MatchStart);
                r.ReplacementText = m.Success && m.Index == r.MatchStart ? Evaluate(m, replacement, options) : replacement;
            }
        }
        return results;
    }

    /// <inheritdoc />
    public int ApplyReplace(IEnumerable<TranslationItem> allItems, IReadOnlyList<SearchResultItem> results, string query, string replacement, SearchOptions options)
    {
        var regex = Build(query, options);
        var lookup = allItems.ToDictionary(i => (i.Namespace, i.Language), i => i);
        var modified = 0;

        foreach (var group in results.Where(r => !r.InKey).GroupBy(r => (r.Key, r.Language)))
        {
            if (!lookup.TryGetValue(group.Key, out var item)) continue;
            var newValue = Replace(regex, item.Value, replacement, options);
            if (newValue == item.Value) continue;
            item.Value = newValue;
            modified++;
        }
        return modified;
    }

    /// <summary>Compiles the query. Literal text is escaped; whole word wraps it in word boundaries.</summary>
    public static Regex Build(string query, SearchOptions options)
    {
        var pattern = options.UseRegex ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { return new Regex(pattern, flags, s_timeout); }
        catch (RegexParseException e) { throw new ArgumentException(e.Message, nameof(query), e); }
    }

    private static string Replace(Regex regex, string value, string replacement, SearchOptions options) =>
        regex.Replace(value, m => Evaluate(m, replacement, options));

    private static string Evaluate(Match m, string replacement, SearchOptions options)
    {
        var text = options.UseRegex ? m.Result(replacement) : replacement; // $1 etc. only mean something in regex mode
        return options.PreserveCase ? MatchCaseOf(m.Value, text) : text;
    }

    /// <summary>"TITLE" → upper, "title" → lower, "Title" → first letter upper; anything else is left as typed.</summary>
    public static string MatchCaseOf(string matched, string replacement)
    {
        var letters = matched.Where(char.IsLetter).ToList();
        if (letters.Count == 0 || replacement.Length == 0) return replacement;
        if (letters.All(char.IsUpper) && letters.Count > 1) return replacement.ToUpperInvariant();
        if (letters.All(char.IsLower)) return replacement.ToLowerInvariant();
        if (char.IsUpper(letters[0]) && letters.Skip(1).All(char.IsLower)) return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    private static IEnumerable<TranslationItem> Filter(IEnumerable<TranslationItem> items, SearchOptions options)
    {
        var languages = options.Languages is { Count: > 0 } l ? new HashSet<string>(l, StringComparer.OrdinalIgnoreCase) : null;
        var include = options.IncludeKeys is { Count: > 0 } inc ? inc.Select(KeyPattern).ToList() : null;
        var exclude = options.ExcludeKeys is { Count: > 0 } exc ? exc.Select(KeyPattern).ToList() : null;

        return items.Where(i =>
            (languages is null || languages.Contains(i.Language))
            && (include is null || include.Any(p => p.IsMatch(i.Namespace)))
            && (exclude is null || !exclude.Any(p => p.IsMatch(i.Namespace))));
    }

    /// <summary>"auth.*" matches by wildcard; a plain "auth" is a prefix, like the old key-path scope.</summary>
    private static Regex KeyPattern(string pattern)
    {
        var hasWildcard = pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal);
        var body = Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal);
        return new Regex(hasWildcard ? $"^{body}$" : $"^{body}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, s_timeout);
    }

    private static void Collect(Regex regex, string text, TranslationItem item, bool inKey, List<SearchResultItem> results)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (Match m in regex.Matches(text))
        {
            if (m.Length == 0) continue; // an empty regex match is not a result
            results.Add(new SearchResultItem
            {
                Key = item.Namespace,
                Language = item.Language,
                Value = text,
                MatchStart = m.Index,
                MatchLength = m.Length,
                InKey = inKey,
            });
        }
    }
}
