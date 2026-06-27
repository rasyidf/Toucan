using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Generic flat JSON profile — one JSON file per language in the root folder.
/// Pattern: {lang}.json (e.g., en.json, fr-FR.json)
/// This is the fallback when no specific framework is detected.
/// </summary>
public class GenericJsonProfile : IFrameworkProfile
{
    public string Id => "generic-json";
    public string DisplayName => "Generic JSON";
    public SaveStyles DefaultFormat => SaveStyles.Json;
    public IEnumerable<string> FilePatterns => ["*.json"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;

        foreach (var file in Directory.GetFiles(rootFolder, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "toucan.project" || name == "package" || name == "tsconfig") continue;
            var lang = ExtractLanguage(Path.GetRelativePath(rootFolder, file));
            if (lang != null)
                yield return new DiscoveredFile(file, Path.GetRelativePath(rootFolder, file), lang);
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        // Skip known non-translation files
        if (name is "toucan.project" or "package" or "package-lock" or "tsconfig") return null;
        return name;
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
        => Path.Combine(rootFolder, $"{language}.json");

    public int DetectionScore(string rootFolder)
    {
        // Lowest priority — always matches if JSON files exist
        if (!Directory.Exists(rootFolder)) return 0;
        return Directory.GetFiles(rootFolder, "*.json").Any(f =>
            Path.GetFileNameWithoutExtension(f) != "toucan.project" &&
            Path.GetFileNameWithoutExtension(f) != "package") ? 1 : 0;
    }
}
