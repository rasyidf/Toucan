using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// i18next framework profile.
/// Conventions:
///   - Nested JSON format
///   - Folder-per-language: locales/{lang}/{namespace}.json OR {lang}/{namespace}.json
///   - Single-file variant: locales/{lang}.json
///   - Plural suffixes: _one, _other, _many, _few, _two, _zero
/// </summary>
public partial class I18nextProfile : IFrameworkProfile, IFrameworkFeatures, IFrameworkDocumentLayout
{
    public string ScannerId => "i18next";
    public Toucan.Plugins.ConfigSchema Settings => FrameworkFeatureSettings.Schema;

    public ResourceDescriptor Describe(DiscoveredFile file) => new()
    {
        Id = file.RelativePath.Replace('\\', '/'), RelativePath = file.RelativePath.Replace('\\', '/'), Language = file.Language,
        FormatId = FormatIds.Json, Dialect = "i18next", Package = file.Package ?? "main",
        SelectorPrefix = file.Package == null ? "" : ResourceEntryOperations.Escape(file.Package) + ".",
        Options = new() { Layout = ResourceLayout.Nested }
    };
    public string Id => "i18next";
    public string DisplayName => "i18next (JSON)";
    public string DefaultFormatId => FormatIds.Namespaced;
    public DiscoveredFile? DescribeDestination(string rootFolder, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var localeRoot = new[] { "locales/", "public/locales/", "src/locales/", "assets/locales/" }
            .FirstOrDefault(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
        if (localeRoot is null || !normalized.EndsWith(".json", StringComparison.Ordinal)) return null;
        var parts = normalized[localeRoot.Length..].Split('/');
        if (parts.Length is not (1 or 2) || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or "..")) return null;
        var stem = Path.GetFileNameWithoutExtension(parts[^1]);
        if (string.IsNullOrWhiteSpace(stem)) return null;
        return new(Path.Combine(rootFolder, normalized.Replace('/', Path.DirectorySeparatorChar)), normalized,
            parts.Length == 1 ? stem : parts[0], parts.Length == 1 ? null : stem);
    }

    public FrameworkDocumentLayout GetDocumentLayout(DiscoveredFile file)
    {
        var parts = file.RelativePath.Replace('\\', '/').Split('/');
        // Discovery emits either <locale-root>/<language>/<package>.json or <locale-root>/<language>.json.
        var family = file.Package is null
            ? string.Join('/', parts[..^1]) + "/@language-file"
            : string.Join('/', parts[..^2]) + "/@package/" + parts[^1];
        return new(family, file.Package is null ? TranslationPath.Empty : new([file.Package]));
    }
    public IEnumerable<string> FilePatterns => ["locales/*/*.json", "locales/*.json", "public/locales/*/*.json"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;

        // Look for common i18next folder structures
        foreach (var localeDir in FindLocaleDirs(rootFolder))
        {
            foreach (var langDir in Directory.GetDirectories(localeDir))
            {
                var lang = Path.GetFileName(langDir);
                foreach (var file in Directory.GetFiles(langDir, "*.json"))
                {
                    var package = Path.GetFileNameWithoutExtension(file);
                    var rel = Path.GetRelativePath(rootFolder, file);
                    yield return new DiscoveredFile(file, rel, lang, package);
                }
            }

            // Also check for single-file-per-lang pattern: locales/en.json
            foreach (var file in Directory.GetFiles(localeDir, "*.json"))
            {
                var lang = Path.GetFileNameWithoutExtension(file);
                var rel = Path.GetRelativePath(rootFolder, file);
                yield return new DiscoveredFile(file, rel, lang);
            }
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        // Pattern: locales/{lang}/ns.json or locales/{lang}.json
        var parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Length >= 3)
            return parts[^2]; // folder name before filename
        if (parts.Length == 2)
            return Path.GetFileNameWithoutExtension(parts[^1]);
        return Path.GetFileNameWithoutExtension(relativePath);
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var ns = package ?? "translation";
        // Prefer existing structure if locales/ dir exists
        var localeDir = FindLocaleDirs(rootFolder).FirstOrDefault() ?? Path.Combine(rootFolder, "locales");
        return Path.Combine(localeDir, language, $"{ns}.json");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;

        // Check for locales/ or public/locales/ directory with lang subfolders containing JSON
        if (FindLocaleDirs(rootFolder).Any()) score += 5;

        // Check for i18next config files
        if (File.Exists(Path.Combine(rootFolder, "i18next.config.js")) ||
            File.Exists(Path.Combine(rootFolder, "i18next.config.ts")) ||
            File.Exists(Path.Combine(rootFolder, "next-i18next.config.js")))
            score += 10;

        // Check package.json for i18next dependency
        var pkgJson = Path.Combine(rootFolder, "package.json");
        if (File.Exists(pkgJson))
        {
            try
            {
                var text = File.ReadAllText(pkgJson);
                if (text.Contains("i18next") || text.Contains("react-i18next") || text.Contains("next-i18next"))
                    score += 8;
            }
            catch { }
        }

        return score;
    }

    private static IEnumerable<string> FindLocaleDirs(string root)
    {
        var candidates = new[] { "locales", "public/locales", "src/locales", "assets/locales" };
        foreach (var c in candidates)
        {
            var dir = Path.Combine(root, c.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir) && (Directory.GetDirectories(dir).Length > 0 || Directory.GetFiles(dir, "*.json").Length > 0))
                yield return dir;
        }
    }
}
