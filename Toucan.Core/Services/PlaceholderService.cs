using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Toucan.Core.Services;

/// <summary>
/// ponytail: advanced placeholder detection, extraction, and validation for translation values.
/// Supports named ({{name}}), indexed ({0}), printf (%s, %d), Ruby/Laravel (:param),
/// ICU ({var, plural/select, ...}), and template literals (${expr}).
/// </summary>
public static partial class PlaceholderService
{
    [GeneratedRegex(@"\{\{[\w.]+\}\}", RegexOptions.Compiled)]
    private static partial Regex NamedDoubleRegex(); // {{name}}

    [GeneratedRegex(@"\{\d+\}", RegexOptions.Compiled)]
    private static partial Regex IndexedRegex(); // {0}, {1}

    [GeneratedRegex(@"\{[\w.]+\}", RegexOptions.Compiled)]
    private static partial Regex NamedSingleRegex(); // {name}

    [GeneratedRegex(@"%(?:\d+\$)?[sdfu@]", RegexOptions.Compiled)]
    private static partial Regex PrintfRegex(); // %s, %d, %1$s

    [GeneratedRegex(@":[a-zA-Z_]\w*", RegexOptions.Compiled)]
    private static partial Regex ColonParamRegex(); // :param

    [GeneratedRegex(@"\$\{[\w.]+\}", RegexOptions.Compiled)]
    private static partial Regex TemplateLiteralRegex(); // ${var}

    [GeneratedRegex(@"\{\w+,\s*(?:plural|select|selectordinal),", RegexOptions.Compiled)]
    private static partial Regex IcuMessageRegex(); // {count, plural, ...}

    /// <summary>Extracts all placeholders from a translation value.</summary>
    public static List<PlaceholderInfo> Extract(string value)
    {
        if (string.IsNullOrEmpty(value)) return [];

        var results = new List<PlaceholderInfo>();
        AddMatches(results, NamedDoubleRegex(), value, PlaceholderType.NamedDouble);
        AddMatches(results, IndexedRegex(), value, PlaceholderType.Indexed);
        AddMatches(results, PrintfRegex(), value, PlaceholderType.Printf);
        AddMatches(results, ColonParamRegex(), value, PlaceholderType.ColonParam);
        AddMatches(results, TemplateLiteralRegex(), value, PlaceholderType.TemplateLiteral);
        AddMatches(results, IcuMessageRegex(), value, PlaceholderType.IcuMessage);

        // Named single last (broader pattern, avoid double-counting indexed)
        foreach (Match m in NamedSingleRegex().Matches(value))
        {
            if (!results.Any(r => r.Position == m.Index))
                results.Add(new PlaceholderInfo(m.Value, PlaceholderType.NamedSingle, m.Index));
        }

        return results.OrderBy(r => r.Position).ToList();
    }

    /// <summary>Validates that target has same placeholders as source (including counts).</summary>
    public static PlaceholderValidation Validate(string source, string target)
    {
        var sourcePlaceholders = Extract(source).Select(p => p.Text).ToList();
        var targetPlaceholders = Extract(target).Select(p => p.Text).ToList();

        // Count-aware comparison: group by text, compare counts
        var sourceGroups = sourcePlaceholders.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        var targetGroups = targetPlaceholders.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());

        var missing = new List<string>();
        var extra = new List<string>();

        foreach (var (placeholder, count) in sourceGroups)
        {
            var targetCount = targetGroups.GetValueOrDefault(placeholder, 0);
            for (int i = 0; i < count - targetCount; i++)
                missing.Add(placeholder);
        }

        foreach (var (placeholder, count) in targetGroups)
        {
            var sourceCount = sourceGroups.GetValueOrDefault(placeholder, 0);
            for (int i = 0; i < count - sourceCount; i++)
                extra.Add(placeholder);
        }

        return new PlaceholderValidation
        {
            IsValid = missing.Count == 0 && extra.Count == 0,
            Missing = missing,
            Extra = extra
        };
    }

    private static void AddMatches(List<PlaceholderInfo> results, Regex regex, string value, PlaceholderType type)
    {
        foreach (Match m in regex.Matches(value))
            results.Add(new PlaceholderInfo(m.Value, type, m.Index));
    }
}

public enum PlaceholderType
{
    NamedDouble,   // {{name}}
    NamedSingle,   // {name}
    Indexed,       // {0}
    Printf,        // %s, %d, %1$s
    ColonParam,    // :param
    TemplateLiteral, // ${var}
    IcuMessage     // {count, plural, ...}
}

public record PlaceholderInfo(string Text, PlaceholderType Type, int Position);

public class PlaceholderValidation
{
    public bool IsValid { get; init; }
    public List<string> Missing { get; init; } = [];
    public List<string> Extra { get; init; } = [];
}
