using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Android framework profile.
/// Conventions:
///   - XML format (strings.xml)
///   - Folder convention: res/values-{lang}/strings.xml (default = res/values/)
///   - Language code uses hyphen-separated BCP-47 variants (e.g., values-zh-rCN)
///   - Supports: string, string-array, plurals, translatable="false"
/// </summary>
public partial class AndroidProfile : IFrameworkProfile
{
    public string Id => "android";
    public string DisplayName => "Android (XML)";
    public SaveStyles DefaultFormat => SaveStyles.AndroidXml;
    public IEnumerable<string> FilePatterns => ["res/values*/strings.xml", "app/src/main/res/values*/strings.xml"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;

        foreach (var resDir in FindResDirs(rootFolder))
        {
            foreach (var valDir in Directory.GetDirectories(resDir, "values*"))
            {
                var stringsFile = Path.Combine(valDir, "strings.xml");
                if (!File.Exists(stringsFile)) continue;

                var lang = ExtractLangFromValuesDir(Path.GetFileName(valDir));
                var rel = Path.GetRelativePath(rootFolder, stringsFile);
                yield return new DiscoveredFile(stringsFile, rel, lang);
            }
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        // Find the values-{lang} folder component
        var parts = relativePath.Replace('\\', '/').Split('/');
        var valuesDir = parts.FirstOrDefault(p => p.StartsWith("values"));
        return valuesDir != null ? ExtractLangFromValuesDir(valuesDir) : null;
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var resDir = FindResDirs(rootFolder).FirstOrDefault() ?? Path.Combine(rootFolder, "res");
        var suffix = language == "default" || string.IsNullOrEmpty(language) ? "" : $"-{ToAndroidLangCode(language)}";
        return Path.Combine(resDir, $"values{suffix}", "strings.xml");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;

        if (FindResDirs(rootFolder).Any()) score += 10;

        // Check for AndroidManifest.xml or build.gradle
        if (File.Exists(Path.Combine(rootFolder, "AndroidManifest.xml")) ||
            File.Exists(Path.Combine(rootFolder, "app", "src", "main", "AndroidManifest.xml")))
            score += 10;
        if (File.Exists(Path.Combine(rootFolder, "build.gradle")) ||
            File.Exists(Path.Combine(rootFolder, "build.gradle.kts")))
            score += 5;

        return score;
    }

    private static string ExtractLangFromValuesDir(string dirName)
    {
        // "values" → default, "values-fr" → fr, "values-zh-rCN" → zh-CN
        if (dirName == "values") return "default";
        var suffix = dirName["values-".Length..];
        // Android uses -r prefix for region: zh-rCN → zh-CN
        return AndroidLangRegex().Replace(suffix, "$1-$2");
    }

    private static string ToAndroidLangCode(string bcp47)
    {
        // BCP-47 "zh-CN" → Android "zh-rCN"
        var parts = bcp47.Split('-');
        if (parts.Length == 2) return $"{parts[0]}-r{parts[1]}";
        return bcp47;
    }

    private static IEnumerable<string> FindResDirs(string root)
    {
        var candidates = new[] { "res", "app/src/main/res", "src/main/res" };
        foreach (var c in candidates)
        {
            var dir = Path.Combine(root, c.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir) && Directory.GetDirectories(dir, "values*").Length > 0)
                yield return dir;
        }
    }

    [GeneratedRegex(@"^(\w+)-r(\w+)$")]
    private static partial Regex AndroidLangRegex();
}
