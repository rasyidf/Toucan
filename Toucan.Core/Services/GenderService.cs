using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// ponytail: detects and manages gendered translation variants.
/// Supports suffix pattern (_male/_female/_other) and ICU {var, select, ...} syntax.
/// </summary>
public static partial class GenderService
{
    public static readonly string[] Genders = ["male", "female", "other"];
    private static readonly string[] Suffixes = Genders.Select(g => "_" + g).ToArray();

    [GeneratedRegex(@"\{(\w+),\s*select,\s*(.+)\}", RegexOptions.Compiled)]
    private static partial Regex IcuSelectRegex();

    public static bool IsGenderedKey(string ns) =>
        Suffixes.Any(s => ns.EndsWith(s, System.StringComparison.OrdinalIgnoreCase));

    public static string GetBaseKey(string ns)
    {
        foreach (var s in Suffixes)
            if (ns.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))
                return ns[..^s.Length];
        return ns;
    }

    public static string? GetGender(string ns)
    {
        foreach (var g in Genders)
            if (ns.EndsWith("_" + g, System.StringComparison.OrdinalIgnoreCase))
                return g;
        return null;
    }

    /// <summary>Generates missing gender forms for a base key.</summary>
    public static List<TranslationItem> GenerateMissingForms(string baseKey, string language, IEnumerable<TranslationItem> existing)
    {
        var existingGenders = existing
            .Where(i => i.Language == language && GetBaseKey(i.Namespace) == baseKey)
            .Select(i => GetGender(i.Namespace))
            .Where(g => g != null)
            .ToHashSet();

        return Genders.Where(g => !existingGenders.Contains(g))
            .Select(g => new TranslationItem
            {
                Namespace = baseKey + "_" + g,
                Language = language,
                Value = string.Empty
            }).ToList();
    }

    /// <summary>Checks if a value contains ICU select syntax.</summary>
    public static bool IsIcuSelect(string value) =>
        !string.IsNullOrEmpty(value) && IcuSelectRegex().IsMatch(value);

    /// <summary>Parses ICU select from a value string.</summary>
    public static Dictionary<string, string>? ParseIcuSelect(string value)
    {
        var match = IcuSelectRegex().Match(value);
        if (!match.Success) return null;

        var forms = new Dictionary<string, string>();
        var body = match.Groups[2].Value;

        int pos = 0;
        while (pos < body.Length)
        {
            while (pos < body.Length && char.IsWhiteSpace(body[pos])) pos++;
            if (pos >= body.Length) break;

            int catStart = pos;
            while (pos < body.Length && body[pos] != '{' && !char.IsWhiteSpace(body[pos])) pos++;
            var cat = body[catStart..pos].Trim();
            if (string.IsNullOrEmpty(cat)) break;

            while (pos < body.Length && body[pos] != '{') pos++;
            if (pos >= body.Length) break;
            pos++;

            int depth = 1, valStart = pos;
            while (pos < body.Length && depth > 0)
            {
                if (body[pos] == '{') depth++;
                else if (body[pos] == '}') depth--;
                if (depth > 0) pos++;
            }
            forms[cat] = body[valStart..pos];
            pos++;
        }
        return forms.Count > 0 ? forms : null;
    }

    /// <summary>Builds an ICU select string from gender-value pairs.</summary>
    public static string BuildIcuSelect(string variable, Dictionary<string, string> forms)
    {
        var pairs = string.Join(" ", forms.Select(kv => $"{kv.Key}{{{kv.Value}}}"));
        return $"{{{variable}, select, {pairs}}}";
    }
}
