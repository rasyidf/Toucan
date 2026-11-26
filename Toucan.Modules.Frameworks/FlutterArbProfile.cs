using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Flutter ARB profile.
/// Convention: lib/l10n/app_{lang}.arb or {lang}.arb
/// Metadata: @-prefixed keys for descriptions/placeholders
/// </summary>
public class FlutterArbProfile : IFrameworkProfile, IFrameworkDocumentLayout
{
    public string Id => "flutter-arb";
    public string DisplayName => "Flutter (ARB)";
    public string DefaultFormatId => FormatIds.Arb;
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
        // Follow the files already there: intl_en.arb gives intl_fr.arb, en.arb gives fr.arb.
        var existing = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.arb").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault() : null;
        var prefix = existing is null ? "app_" : Prefix(existing);
        return Path.Combine(dir, $"{prefix}{langCode}.arb");
    }

    /// <summary>The ARB files of one folder that share a prefix (app_en.arb, app_fr.arb) are the languages of one resource.</summary>
    public FrameworkDocumentLayout GetDocumentLayout(DiscoveredFile file)
    {
        var relative = file.RelativePath.Replace('\\', '/');
        var slash = relative.LastIndexOf('/');
        var folder = slash < 0 ? "" : relative[..slash];
        return new(folder + "/@arb/" + Prefix(Path.GetFileNameWithoutExtension(relative)), TranslationPath.Empty);
    }

    public DiscoveredFile? DescribeDestination(string rootFolder, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.EndsWith(".arb", StringComparison.Ordinal) || normalized.Split('/').Any(p => p is "" or "." or "..")) return null;
        var language = ExtractLanguage(normalized);
        return string.IsNullOrWhiteSpace(language) ? null
            : new(Path.Combine(rootFolder, normalized.Replace('/', Path.DirectorySeparatorChar)), normalized, language);
    }

    private static string Prefix(string fileName)
    {
        var idx = fileName.IndexOf('_');
        return idx < 0 ? "" : fileName[..(idx + 1)];
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
