using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Toucan.Avalonia.Locales;

/// <summary>
/// UI localization. The English text is the key: <see cref="T"/> returns the translation when the current language
/// has one and the English text otherwise, so a missing translation never shows an empty label. Translations live in
/// <c>Locales/Strings.{culture}.json</c> (embedded, a flat English → translated map) and are read once at startup.
/// </summary>
internal static class Loc
{
    private static FrozenDictionary<string, string> s_table = FrozenDictionary<string, string>.Empty;

    /// <summary>True once <see cref="Use"/> ran, so startup code can tell an explicit choice from the default.</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>Languages that ship a translation table, for the language picker.</summary>
    public static IReadOnlyList<string> Available { get; } = ["en-US", "id-ID"];

    /// <summary>Switches the UI language (affects strings read after this call) and the process UI culture.</summary>
    public static void Use(string? language)
    {
        IsInitialized = true;
        CultureInfo culture;
        try
        {
            culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        s_table = Load(culture);
        s_formats.Clear();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Text.CompositeFormat> s_formats = new();

    /// <summary>Formats a translated template such as <c>"Page {0} of {1}"</c> with the current culture.</summary>
    public static string Format(string english, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, s_formats.GetOrAdd(english, k => System.Text.CompositeFormat.Parse(T(k))), args);

    public static string T(string english) => s_table.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>Loads Strings.{culture}.json, falling back to the neutral language (id) when only that exists.</summary>
    private static FrozenDictionary<string, string> Load(CultureInfo culture)
    {
        foreach (var name in new[] { culture.Name, culture.TwoLetterISOLanguageName })
        {
            if (string.IsNullOrEmpty(name)) continue;
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Toucan.Avalonia.Locales.Strings.{name}.json");
            if (stream == null) continue;
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (map != null) return map.ToFrozenDictionary(StringComparer.Ordinal);
        }
        return FrozenDictionary<string, string>.Empty;
    }

    /// <summary>Every embedded translation table, keyed by culture name. Used by tests to check completeness.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var resource in typeof(Loc).Assembly.GetManifestResourceNames())
        {
            const string prefix = "Toucan.Avalonia.Locales.Strings.";
            if (!resource.StartsWith(prefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(resource)!;
            result[resource[prefix.Length..^".json".Length]] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        return result;
    }
}

/// <summary>XAML: <c>Text="{loc:Loc 'Save'}"</c> looks the text up in the current language.</summary>
public sealed class LocExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}

/// <summary>Translates a bound string, for lists whose items are English strings (such as the settings pages).</summary>
public sealed class LocConverter : IValueConverter
{
    public static LocConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? Loc.T(s) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
