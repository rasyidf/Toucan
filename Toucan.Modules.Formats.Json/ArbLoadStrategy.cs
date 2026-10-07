using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>
/// Loads Flutter ARB (Application Resource Bundle) JSON files. The <c>@key</c> metadata object of each
/// message and the other <c>@@</c> header entries go in <see cref="TranslationItem.FormatData"/> under the
/// <see cref="ArbFormat"/> keys so the save strategy can write them back.
/// </summary>
public class ArbLoadStrategy : ILoadStrategy
{
    private static readonly Regex s_scriptOrRegion = new("^([A-Z][a-z]{3}|[A-Z]{2}|[0-9]{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_languageCode = new("^[a-z]{2,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string FormatId => FormatIds.Arb;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.arb", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(content);

                // @@locale wins; otherwise the locale comes from the file name (app_en.arb → en, app_en_US.arb → en_US)
                var lang = "unknown";
                if (doc.RootElement.TryGetProperty("@@locale", out var localeProp))
                    lang = localeProp.GetString() ?? "unknown";
                else
                    lang = LocaleFromFileName(Path.GetFileNameWithoutExtension(file)) ?? lang;

                var relative = Path.GetRelativePath(folder, file);
                var header = new Dictionary<string, JsonElement>();
                var metadata = new Dictionary<string, string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.StartsWith("@@", StringComparison.Ordinal))
                    {
                        if (prop.Name != "@@locale") header[prop.Name] = prop.Value;
                    }
                    else if (prop.Name.StartsWith('@'))
                        metadata[prop.Name[1..]] = prop.Value.GetRawText();
                }
                var headerJson = header.Count == 0 ? null : JsonSerializer.Serialize(header);

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.StartsWith('@')) continue;
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;

                    var data = new Dictionary<string, string> { [ArbFormat.File] = relative };
                    if (metadata.TryGetValue(prop.Name, out var meta)) data[ArbFormat.Metadata] = meta;
                    if (headerJson != null) data[ArbFormat.Header] = headerJson;

                    items.Add(new TranslationItem { Language = lang, Namespace = prop.Name, Value = prop.Value.GetString() ?? "", FormatData = data });
                }
            }
            catch { }
        }
        return items;
    }

    /// <summary>
    /// Finds the locale in <c>app_en</c>, <c>app_en_US</c> or <c>intl_zh_Hans_CN</c>: the first part that is a
    /// language code and is followed only by script/region parts. Falls back to the last part.
    /// </summary>
    internal static string? LocaleFromFileName(string name)
    {
        var parts = name.Split('_');
        if (parts.Length < 2) return null;

        for (var i = 1; i < parts.Length; i++)
        {
            if (!s_languageCode.IsMatch(parts[i])) continue;
            if (parts.Skip(i + 1).All(p => s_scriptOrRegion.IsMatch(p))) return string.Join('_', parts.Skip(i));
        }
        return parts[^1];
    }
}

/// <summary>Keys used in <see cref="TranslationItem.FormatData"/> for ARB entries.</summary>
public static class ArbFormat
{
    public const string File = "arb.file";
    /// <summary>The raw JSON of the message's <c>@key</c> object.</summary>
    public const string Metadata = "arb.metadata";
    /// <summary>The raw JSON of the file's <c>@@</c> entries other than <c>@@locale</c>; repeated on every item of the file so it survives when some are empty.</summary>
    public const string Header = "arb.header";
}
