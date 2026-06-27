using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Ruby on Rails / generic YAML locale profile.
/// Convention: config/locales/{lang}.yml or locales/{lang}.yml
/// YAML root key is the language code.
/// </summary>
public class RailsYamlProfile : IFrameworkProfile
{
    public string Id => "rails-yaml";
    public string DisplayName => "Rails (YAML)";
    public SaveStyles DefaultFormat => SaveStyles.Yaml;
    public IEnumerable<string> FilePatterns => ["config/locales/*.yml", "locales/*.yml", "*.yml"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        foreach (var dir in FindLocaleDirs(rootFolder))
            foreach (var file in Directory.GetFiles(dir, "*.yml").Concat(Directory.GetFiles(dir, "*.yaml")))
            {
                var lang = ExtractLanguage(Path.GetRelativePath(rootFolder, file));
                if (lang != null)
                    yield return new DiscoveredFile(file, Path.GetRelativePath(rootFolder, file), lang);
            }
    }

    public string? ExtractLanguage(string relativePath)
        => Path.GetFileNameWithoutExtension(relativePath);

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var dir = FindLocaleDirs(rootFolder).FirstOrDefault() ?? Path.Combine(rootFolder, "config", "locales");
        return Path.Combine(dir, $"{language}.yml");
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) return 0;
        var score = 0;
        if (FindLocaleDirs(rootFolder).Any()) score += 5;
        if (File.Exists(Path.Combine(rootFolder, "Gemfile"))) score += 8;
        if (Directory.Exists(Path.Combine(rootFolder, "config", "locales"))) score += 10;
        return score;
    }

    private static IEnumerable<string> FindLocaleDirs(string root)
    {
        var candidates = new[] { "config/locales", "locales", "i18n" };
        foreach (var c in candidates)
        {
            var dir = Path.Combine(root, c.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dir) &&
                (Directory.GetFiles(dir, "*.yml").Length > 0 || Directory.GetFiles(dir, "*.yaml").Length > 0))
                yield return dir;
        }
    }
}
