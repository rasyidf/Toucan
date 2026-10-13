namespace Toucan.Core.Commands;

/// <summary>
/// Portable shortcut text such as <c>Mod+Shift+K</c>. The key token is not interpreted here (the UI layer knows its key
/// names); modifiers are validated, spelled canonically and ordered, so equal shortcuts compare equal.
/// </summary>
public static class ShortcutText
{
    private static readonly string[] s_modifierOrder = ["Mod", "Ctrl", "Alt", "Shift", "Meta"];

    /// <summary>Normalizes <paramref name="text"/>; false when it is empty, repeats a modifier or has no key.</summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = new HashSet<string>(StringComparer.Ordinal);
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return false;
            var modifier = s_modifierOrder.FirstOrDefault(m => string.Equals(m, raw, StringComparison.OrdinalIgnoreCase))
                           ?? (string.Equals(raw, "Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : null)
                           ?? (string.Equals(raw, "Cmd", StringComparison.OrdinalIgnoreCase) ? "Meta" : null);
            if (modifier is not null)
            {
                if (!modifiers.Add(modifier)) return false;
            }
            else
            {
                if (key is not null) return false;
                key = raw.Length == 1 ? raw.ToUpperInvariant() : raw;
            }
        }
        if (key is null) return false;

        normalized = string.Join('+', s_modifierOrder.Where(modifiers.Contains).Append(key));
        return true;
    }

    /// <summary>Replaces <c>Mod</c> with the platform's primary modifier so shortcuts can be compared for conflicts.</summary>
    public static string Resolve(string normalized, bool isMac)
    {
        var parts = normalized.Split('+').Select(p => p == "Mod" ? (isMac ? "Meta" : "Ctrl") : p).ToList();
        var key = parts[^1];
        var mods = parts.Take(parts.Count - 1).Distinct().OrderBy(m => Array.IndexOf(s_modifierOrder, m));
        return string.Join('+', mods.Append(key));
    }
}
