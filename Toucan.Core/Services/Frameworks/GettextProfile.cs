using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Gettext PO/POT profile.
/// Convention: locale/{lang}/LC_MESSAGES/{domain}.po or {lang}.po
/// </summary>
public class GettextProfile : IFrameworkProfile
{
    public string Id => "gettext";
    public string DisplayName => "Gettext (PO)";
    public SaveStyles DefaultFormat => SaveStyles.Properties;
    public IEnumerable<string> FilePatterns => ["locale/**//*.po", "**/*.po"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        var files = Directory.GetFiles(rootFolder, "*.po", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(rootFolder, "*.pot", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(rootFolder, file);
            var lang = ExtractLanguage(rel) ?? Path.GetFileNameWithoutExtension(file);
            var package = Path.GetFileNameWithoutExtension(file);
            yield return new DiscoveredFile(file, rel, lang, package);
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        // locale/fr/LC_MESSAGES/messages.po → fr
        // locale/fr_FR/messages.po → fr-FR
        // fr.po → fr
        var parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Length >= 3)
        {
            // Try: locale/{lang}/... pattern
            var candidate = parts[^3] != "locale" ? parts[^2] : parts[^2];
            // Look for LC_MESSAGES parent or just lang folder
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "locale" || parts[i] == "locales" || parts[i] == "po")
                    return NormalizeLang(parts[i + 1]);
            }
        }
        return parts.Length == 1 ? NormalizeLang(Path.GetFileNameWithoutExtension(parts[0])) : NormalizeLang(parts[^2]);
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var domain = package ?? "messages";
        var localeDir = Path.Combine(rootFolder, "locale");
        if (Directory.Exists(localeDir))
            return Path.Combine(localeDir, language.Replace('-', '_'), "LC_MESSAGES", $"{domain}.po");
        return Path.Combine(rootFolder, $"{language}.po");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;
        if (Directory.GetFiles(rootFolder, "*.po", SearchOption.AllDirectories).Length > 0) score += 8;
        if (Directory.GetFiles(rootFolder, "*.pot", SearchOption.AllDirectories).Length > 0) score += 5;
        if (Directory.Exists(Path.Combine(rootFolder, "locale"))) score += 5;
        return score;
    }

    private static string NormalizeLang(string code)
    {
        // fr_FR → fr-FR, LC_MESSAGES → skip
        if (code == "LC_MESSAGES") return "default";
        return code.Replace('_', '-');
    }
}
