using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// .NET RESX profile.
/// Convention: Resources/{Name}.{lang}.resx (default = Resources/{Name}.resx)
/// </summary>
public partial class DotNetResxProfile : IFrameworkProfile
{
    public string Id => "dotnet-resx";
    public string DisplayName => ".NET (RESX)";
    public SaveStyles DefaultFormat => SaveStyles.Resx;
    public IEnumerable<string> FilePatterns => ["**/*.resx"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        foreach (var file in Directory.GetFiles(rootFolder, "*.resx", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(rootFolder, file);
            var lang = ExtractLanguage(rel) ?? "default";
            var package = GetResourceName(rel);
            yield return new DiscoveredFile(file, rel, lang, package);
        }
    }

    public string? ExtractLanguage(string relativePath)
    {
        // Strings.fr-FR.resx → fr-FR, Strings.resx → null (default)
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var match = LangSuffixRegex().Match(name);
        return match.Success ? match.Groups[1].Value : null;
    }

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var resName = package ?? "Strings";
        var suffix = language == "default" || string.IsNullOrEmpty(language) ? "" : $".{language}";
        return Path.Combine(rootFolder, "Resources", $"{resName}{suffix}.resx");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;
        if (Directory.GetFiles(rootFolder, "*.resx", SearchOption.AllDirectories).Length > 0) score += 10;
        if (Directory.GetFiles(rootFolder, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0) score += 5;
        return score;
    }

    private static string GetResourceName(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        // Strip language suffix: Strings.fr-FR → Strings
        var match = LangSuffixRegex().Match(name);
        return match.Success ? name[..match.Index] : name;
    }

    [GeneratedRegex(@"\.([a-z]{2}(?:-[A-Za-z]{2,})?)$")]
    private static partial Regex LangSuffixRegex();
}
