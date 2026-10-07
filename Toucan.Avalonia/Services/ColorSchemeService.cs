using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using Toucan.Core.Options;

namespace Toucan.Avalonia.Services;

/// <summary>A named accent color the user can pick as a starting point.</summary>
public sealed record ColorSchemePreset(string Name, string Accent)
{
    public IBrush Swatch { get; } = Brush.Parse(Accent);
}

/// <summary>One editable theme color: the resource key and a label for the editor.</summary>
public sealed record SchemeToken(string Key, string Label);

/// <summary>
/// Applies the user's color scheme: the accent color (through FluentAvalonia) and overrides for the app's own theme brushes.
/// The original colors come from App.axaml and are captured the first time anything is applied, so "reset" always has a target.
/// </summary>
public static class ColorSchemeService
{
    public const string Custom = "Custom";

    public static IReadOnlyList<ColorSchemePreset> Presets { get; } =
    [
        new("Toucan", "#0097A7"), new("Ocean", "#2F6FDB"), new("Forest", "#2E8B57"), new("Sunset", "#D9822B"),
        new("Rose", "#D6336C"), new("Violet", "#7C4DFF"), new("Graphite", "#607D8B"),
    ];

    /// <summary>Preset names plus "Custom", for the picker.</summary>
    public static IReadOnlyList<string> SchemeNames { get; } = [.. Presets.Select(p => p.Name), Custom];

    public static IReadOnlyList<SchemeToken> Tokens { get; } =
    [
        new("ChromeBackgroundBrush", "Window background"),
        new("EditorBackgroundBrush", "Editor and sidebar background"),
        new("CardBackgroundBrush", "Card background"),
        new("CardBorderBrush", "Card border"),
        new("MutedTextBrush", "Secondary text"),
        new("GoodBrush", "Success"),
        new("WarnBrush", "Warning"),
        new("BadBrush", "Error"),
        new("InfoBrush", "Info"),
    ];

    /// <summary>Brushes that mirror an editable one (the Settings* tokens are separate resources with the same colors).</summary>
    private static readonly Dictionary<string, string[]> s_aliases = new()
    {
        ["CardBackgroundBrush"] = ["SettingsCardBackgroundBrush"],
        ["CardBorderBrush"] = ["SettingsCardBorderBrush"],
        ["MutedTextBrush"] = ["SettingsHeaderForegroundBrush"],
    };

    private static readonly Dictionary<(ThemeVariant Variant, string Key), Color> s_defaults = [];

    public static string VariantName(ThemeVariant variant) => variant == ThemeVariant.Dark ? "Dark" : "Light";

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static ColorSchemePreset? PresetNamed(string? name) => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The color a token has with no user edits.</summary>
    public static Color DefaultColor(ThemeVariant variant, string key)
    {
        EnsureDefaults();
        return s_defaults.TryGetValue((variant, key), out var c) ? c : Colors.Gray;
    }

    /// <summary>Parses #RGB, #RRGGBB or #AARRGGBB; the result is always opaque.</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text) || !Color.TryParse(text.Trim(), out var parsed)) return false;
        color = Color.FromRgb(parsed.R, parsed.G, parsed.B);
        return true;
    }

    public static void Apply(AppOptions options) => Apply(options.ColorScheme, options.AccentColor, options.SchemeColors);

    public static void Apply(string? scheme, string? accentHex, IReadOnlyDictionary<string, string>? overrides)
    {
        if (Application.Current is not { } app) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var copy = overrides is null ? null : new Dictionary<string, string>(overrides);
            Dispatcher.UIThread.Post(() => Apply(scheme, accentHex, copy));
            return;
        }
        EnsureDefaults();

        var accent = TryParseColor(accentHex, out var custom) ? custom
            : TryParseColor(PresetNamed(scheme)?.Accent ?? Presets[0].Accent, out var preset) ? preset : Colors.Teal;
        if (app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } fluent) fluent.CustomAccentColor = accent;

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) is false || provider is not ResourceDictionary dict) continue;
            foreach (var token in Tokens)
            {
                if (!s_defaults.TryGetValue((variant, token.Key), out var original)) continue; // never paint a color we could not read
                var color = overrides is not null && overrides.TryGetValue($"{VariantName(variant)}:{token.Key}", out var hex) && TryParseColor(hex, out var edited)
                    ? edited : original;
                Set(dict, token.Key, color);
                if (s_aliases.TryGetValue(token.Key, out var aliases)) foreach (var alias in aliases) Set(dict, alias, color);
            }
            // Selection highlight follows the accent.
            Set(dict, "SelectedBackgroundBrush", Color.FromArgb(variant == ThemeVariant.Dark ? (byte)0x33 : (byte)0x1A, accent.R, accent.G, accent.B));
        }
    }

    private static void Set(ResourceDictionary dict, string key, Color color) => dict[key] = new SolidColorBrush(color);

    /// <summary>Remembers the App.axaml color of every token not seen yet. Runs before the first edit is applied, so it always reads originals.</summary>
    private static void EnsureDefaults()
    {
        if (Application.Current is not { } app) return;
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary dict) continue;
            foreach (var token in Tokens)
                if (!s_defaults.ContainsKey((variant, token.Key)) && dict.TryGetValue(token.Key, out var value) && value is ISolidColorBrush brush)
                    s_defaults[(variant, token.Key)] = brush.Color;
        }
    }
}
