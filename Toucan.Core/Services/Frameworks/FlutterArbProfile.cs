using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Flutter ARB profile.
/// Convention: lib/l10n/app_{lang}.arb or {lang}.arb
/// Metadata: @-prefixed keys for descriptions/placeholders
/// </summary>
public class FlutterArbProfile : IFrameworkProfile
{
    public string Id => "flutter-arb";
    public string DisplayName => "Flutter (ARB)";
    public SaveStyles DefaultFormat => SaveStyles.Arb;
    public IEnumerable<string> FilePatterns => ["lib/l10n/*.arb", "l10n/*.arb", "*.arb"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        foreach (var dir in FindArbDirs(rootFolder))
            foreach (var file in Directory.GetFiles(dir, "*.arb"))
            {
                var lang = ExtractLanguage(Path.GetRelativePath(rootFolder, file));
                if (lang != null)
                    yield return new DiscoveredFile(file, Path.GetRelativePath(rootFolder, file), lang);
            }
    }

    public string? ExtractLanguage(string relativePath)
    {
        // app_en.arb → en, app_zh_CN.arb → zh-CN, intl_fr.arb → fr
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var idx = name.IndexOf('_');
        if (idx < 0) return name;
        var langPart = name[(idx + 1)..];
        // Convert underscore region to hyphen: zh_CN → zh-CN
        return langPart.Replace('_', '-');
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var dir = FindArbDirs(rootFolder).FirstOrDefault() ?? Path.Combine(rootFolder, "lib", "l10n");
        var langCode = language.Replace('-', '_');
        return Path.Combine(dir, $"app_{langCode}.arb");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;
        if (FindArbDirs(rootFolder).Any()) score += 8;
        if (File.Exists(Path.Combine(rootFolder, "pubspec.yaml"))) score += 10;
        if (File.Exists(Path.Combine(rootFolder, "l10n.yaml"))) score += 10;
        return score;
    }

    private static IEnumerable<string> FindArbDirs(string root)
    {
        var candidates = new[] { "lib/l10n", "l10n", "lib/src/l10n", "." };
        foreach (var c in candidates)
        {
            var dir = Path.Combine(root, c.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.arb").Length > 0)
                yield return dir;
        }
    }
}
