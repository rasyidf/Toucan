namespace Toucan.Core.Models;

/// <summary>
/// Stable string identifiers for translation file formats. Built-ins map 1:1 to <see cref="SaveStyles"/>;
/// plugin formats supply their own IDs. IDs are compared case-insensitively.
/// </summary>
public static class FormatIds
{
    public const string Json = "json";
    public const string Namespaced = "namespaced";
    public const string Po = "po";
    public const string Yaml = "yaml";
    public const string Ini = "ini";
    public const string Toml = "toml";
    public const string AndroidXml = "android-xml";
    public const string IosStrings = "ios-strings";
    public const string Xliff = "xliff";
    public const string Arb = "arb";
    public const string Csv = "csv";
    public const string Resx = "resx";
    public const string JavaProperties = "java-properties";
    public const string LaravelPhp = "laravel-php";

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    private static readonly Dictionary<SaveStyles, string> s_byStyle = new()
    {
        [SaveStyles.Json] = Json,
        [SaveStyles.Namespaced] = Namespaced,
        [SaveStyles.Properties] = Po,
        [SaveStyles.Yaml] = Yaml,
        [SaveStyles.Adb] = Ini,
        [SaveStyles.Toml] = Toml,
        [SaveStyles.AndroidXml] = AndroidXml,
        [SaveStyles.IosStrings] = IosStrings,
        [SaveStyles.Xliff] = Xliff,
        [SaveStyles.Arb] = Arb,
        [SaveStyles.Csv] = Csv,
        [SaveStyles.Resx] = Resx,
        [SaveStyles.JavaProperties] = JavaProperties,
        [SaveStyles.LaravelPhp] = LaravelPhp,
    };

    private static readonly Dictionary<string, SaveStyles> s_byId =
        s_byStyle.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public static string FromStyle(SaveStyles style) =>
        s_byStyle.TryGetValue(style, out var id) ? id : throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown SaveStyles value.");

    public static bool TryGetStyle(string? formatId, out SaveStyles style)
    {
        if (formatId is not null && s_byId.TryGetValue(formatId, out style)) return true;
        style = default;
        return false;
    }

    /// <summary>Maps a legacy persisted value (enum number or enum name) to a format ID, or null if unrecognized.</summary>
    public static string? FromLegacy(System.Text.Json.JsonElement value)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var n)
            && Enum.IsDefined(typeof(SaveStyles), n))
            return FromStyle((SaveStyles)n);

        if (value.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var text = value.GetString();
            if (Enum.TryParse<SaveStyles>(text, ignoreCase: true, out var s) && Enum.IsDefined(s))
                return FromStyle(s);
            // Not an enum name: assume it already is a format ID (e.g. written by a plugin-aware build).
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }
}
