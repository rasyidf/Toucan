using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

internal static class NestedJsonParser
{
    public static List<TranslationItem> ParseNestedJson(string language, string jsonContent, ILogger? logger = null)
    {
        var result = new List<TranslationItem>();
        if (string.IsNullOrWhiteSpace(jsonContent)) return result;

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            WalkElement(language, doc.RootElement, "", result);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse nested JSON for language {Language}", language);
        }

        return result;
    }

    private static void WalkElement(string language, JsonElement element, string path, List<TranslationItem> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
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

            // Skip null/array/undefined
            case JsonValueKind.Array:
                int idx = 0;
                foreach (var arrayItem in element.EnumerateArray())
                {
                    var itemPath = string.IsNullOrEmpty(path) ? $"[{idx}]" : $"{path}[{idx}]";
                    WalkElement(language, arrayItem, itemPath, result);
                    idx++;
                }
                break;
        }
    }
}
