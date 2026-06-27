using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// iOS/macOS .strings profile.
/// Convention: {lang}.lproj/Localizable.strings (or other .strings files)
/// Default language: Base.lproj/ or en.lproj/
/// </summary>
public class IosProfile : IFrameworkProfile
{
    public string Id => "ios-strings";
    public string DisplayName => "iOS (.strings)";
    public SaveStyles DefaultFormat => SaveStyles.IosStrings;
    public IEnumerable<string> FilePatterns => ["*.lproj/*.strings"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        foreach (var lproj in Directory.GetDirectories(rootFolder, "*.lproj", SearchOption.AllDirectories))
        {
            var lang = ExtractLangFromLproj(Path.GetFileName(lproj));
            foreach (var file in Directory.GetFiles(lproj, "*.strings"))
            {
                var package = Path.GetFileNameWithoutExtension(file);
                var rel = Path.GetRelativePath(rootFolder, file);
                yield return new DiscoveredFile(file, rel, lang, package);
            }
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        var lproj = parts.FirstOrDefault(p => p.EndsWith(".lproj"));
        return lproj != null ? ExtractLangFromLproj(lproj) : null;
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var fileName = package ?? "Localizable";
        return Path.Combine(rootFolder, $"{language}.lproj", $"{fileName}.strings");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;
        if (Directory.GetDirectories(rootFolder, "*.lproj", SearchOption.AllDirectories).Length > 0) score += 10;
        if (File.Exists(Path.Combine(rootFolder, "Info.plist")) ||
            Directory.GetFiles(rootFolder, "*.xcodeproj", SearchOption.TopDirectoryOnly).Length > 0 ||
            Directory.GetDirectories(rootFolder, "*.xcodeproj", SearchOption.TopDirectoryOnly).Length > 0)
            score += 8;
        return score;
    }

    private static string ExtractLangFromLproj(string dirName)
    {
        // en.lproj → en, Base.lproj → default, pt-BR.lproj → pt-BR
        var lang = dirName.Replace(".lproj", "");
        return lang == "Base" ? "default" : lang;
    }
}
