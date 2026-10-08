using System.Globalization;

namespace Toucan.Plugins;

/// <summary>Picks text by culture for titles that carry translations.</summary>
public static class LocalizedText
{
    /// <summary>The entry for <paramref name="culture"/>, else for its parent cultures, else <paramref name="fallback"/>.</summary>
    public static string Pick(IReadOnlyDictionary<string, string>? table, string fallback, CultureInfo? culture = null)
    {
        if (table is null || table.Count == 0) return fallback;
        for (var c = culture ?? CultureInfo.CurrentUICulture; c.Name.Length > 0; c = c.Parent)
            if (table.TryGetValue(c.Name, out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return fallback;
    }
}
