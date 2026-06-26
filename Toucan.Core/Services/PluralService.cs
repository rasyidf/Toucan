using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// ponytail: detects and manages plural forms in translations.
/// Supports i18next suffix pattern (_one/_other) and ICU {count, plural, ...} syntax.
/// Ceiling: no CLDR rule validation — just groups and generates common forms.
/// </summary>
public static partial class PluralService
{
    // CLDR plural categories
    public static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];

    // i18next suffixes
    private static readonly string[] Suffixes = Categories.Select(c => "_" + c).ToArray();

    [GeneratedRegex(@"\{(\w+),\s*plural,\s*(.+)\}", RegexOptions.Compiled)]
    private static partial Regex IcuPluralRegex();

    /// <summary>Checks if a key is a plural variant (has _one, _other, etc. suffix).</summary>
    public static bool IsPluralKey(string ns) =>
        Suffixes.Any(s => ns.EndsWith(s, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets the base key (without plural suffix).</summary>
    public static string GetBaseKey(string ns)
    {
        foreach (var s in Suffixes)
            if (ns.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))
                return ns[..^s.Length];
        return ns;
    }

    /// <summary>Gets the plural category from a suffixed key.</summary>
    public static string? GetCategory(string ns)
    {
        foreach (var cat in Categories)
            if (ns.EndsWith("_" + cat, System.StringComparison.OrdinalIgnoreCase))
                return cat;
        return null;
    }

    /// <summary>Groups translations by their plural base key.</summary>
    public static Dictionary<string, List<TranslationItem>> GroupPlurals(IEnumerable<TranslationItem> items)
    {
        return items.Where(i => IsPluralKey(i.Namespace))
            .GroupBy(i => GetBaseKey(i.Namespace) + "|" + i.Language)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Generates missing plural forms for a base key in a given language.</summary>
    public static List<TranslationItem> GenerateMissingForms(string baseKey, string language, IEnumerable<TranslationItem> existing)
    {
        var existingCats = existing
            .Where(i => i.Language == language && GetBaseKey(i.Namespace) == baseKey)
            .Select(i => GetCategory(i.Namespace))
            .Where(c => c != null)
            .ToHashSet();

        var missing = new List<TranslationItem>();
        // At minimum, ensure _one and _other exist
        foreach (var cat in new[] { "one", "other" })
        {
            if (!existingCats.Contains(cat))
            {
                missing.Add(new TranslationItem
                {
                    Namespace = baseKey + "_" + cat,
                    Language = language,
                    Value = string.Empty
                });
            }
        }
        return missing;
    }

    /// <summary>Checks if a value contains ICU plural syntax.</summary>
    public static bool IsIcuPlural(string value) =>
        !string.IsNullOrEmpty(value) && IcuPluralRegex().IsMatch(value);

    /// <summary>Parses ICU plural forms from a value string.</summary>
    public static Dictionary<string, string>? ParseIcuPlural(string value)
    {
        var match = IcuPluralRegex().Match(value);
        if (!match.Success) return null;

        var forms = new Dictionary<string, string>();
        var body = match.Groups[2].Value;

        // Parse "one{# item} other{# items}" pattern
        int pos = 0;
        while (pos < body.Length)
        {
            // skip whitespace
            while (pos < body.Length && char.IsWhiteSpace(body[pos])) pos++;
            if (pos >= body.Length) break;

            // read category name
            int catStart = pos;
            while (pos < body.Length && body[pos] != '{' && !char.IsWhiteSpace(body[pos])) pos++;
            var cat = body[catStart..pos].Trim();
            if (string.IsNullOrEmpty(cat)) break;

            // skip to opening brace
            while (pos < body.Length && body[pos] != '{') pos++;
            if (pos >= body.Length) break;
            pos++; // skip {

            // read until matching }
            int depth = 1;
            int valStart = pos;
            while (pos < body.Length && depth > 0)
            {
                if (body[pos] == '{') depth++;
                else if (body[pos] == '}') depth--;
                if (depth > 0) pos++;
            }
            forms[cat] = body[valStart..pos];
            pos++; // skip }
        }
        return forms.Count > 0 ? forms : null;
    }

    /// <summary>Builds an ICU plural string from category-value pairs.</summary>
    public static string BuildIcuPlural(string variable, Dictionary<string, string> forms)
    {
        var pairs = string.Join(" ", forms.Select(kv => $"{kv.Key}{{{kv.Value}}}"));
        return $"{{{variable}, plural, {pairs}}}";
    }
}
