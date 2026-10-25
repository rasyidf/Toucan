using Microsoft.Extensions.Logging;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class NestedJsonParser
{
    /// <summary><see cref="TranslationItem.FormatData"/> key holding the raw JSON of a number or boolean value.</summary>
    public const string JsonScalarKey = "json.scalar";

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
            return ResourceJsonCodec.Read(jsonContent, new ResourceDescriptor { Id = "", Language = internedLang, FormatId = FormatIds.Json }).Entries.ToList();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse nested JSON for language {Language}", language);
            return [];
        }
    }

}
