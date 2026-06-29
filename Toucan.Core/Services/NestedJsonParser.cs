using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

internal static class NestedJsonParser
{
    // ponytail: intern language strings to avoid N duplicates in memory for projects with many files per language
    private static readonly Dictionary<string, string> s_internedLangs = new(StringComparer.Ordinal);

    public static List<TranslationItem> ParseNestedJson(string language, string jsonContent, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return [];

        // Intern the language string so all items from all files share the same instance
        if (!s_internedLangs.TryGetValue(language, out var internedLang))
        {
            internedLang = string.Intern(language);
            s_internedLangs[internedLang] = internedLang;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            // Pre-estimate capacity: average JSON object has ~50 leaf keys
            var result = new List<TranslationItem>(64);
            WalkElement(internedLang, doc.RootElement, "", result);
            return result;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse nested JSON for language {Language}", language);
            return [];
        }
    }

    private static void WalkElement(string language, JsonElement element, string path, List<TranslationItem> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? prop.Name : string.Concat(path, ".", prop.Name);
                    WalkElement(language, prop.Value, childPath, result);
                }
                break;

            case JsonValueKind.String:
                result.Add(new TranslationItem { Namespace = path, Value = element.GetString() ?? "", Language = language });
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result.Add(new TranslationItem { Namespace = path, Value = element.ToString(), Language = language });
                break;

            case JsonValueKind.Array:
                int idx = 0;
                foreach (var arrayItem in element.EnumerateArray())
                {
                    var itemPath = path.Length == 0 ? $"[{idx}]" : string.Concat(path, "[", idx.ToString(), "]");
                    WalkElement(language, arrayItem, itemPath, result);
                    idx++;
                }
                break;
        }
    }
}
