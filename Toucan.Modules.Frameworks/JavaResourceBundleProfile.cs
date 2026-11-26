using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Frameworks;

/// <summary>
/// Java ResourceBundle: <c>messages.properties</c> (the base bundle, language <c>default</c>), <c>messages_fr.properties</c>,
/// <c>messages_fr_CA.properties</c>, or files named only by their locale (<c>fr.properties</c>). Files of one folder with the same
/// base name are the languages of one resource.
/// </summary>
public partial class JavaResourceBundleProfile : IFrameworkDocumentLayout
{
    /// <summary>The language of a base bundle without a locale suffix, as for neutral RESX resources.</summary>
    public const string BaseLanguage = "default";

    private static readonly string[] s_skipped = ["target", "build", "out", "bin", "obj", "node_modules", ".git", ".gradle", ".idea", ".toucan"];

    public string Id => "java-resourcebundle";
    public string DisplayName => "Java (ResourceBundle)";
    public string DefaultFormatId => FormatIds.JavaProperties;
    public IEnumerable<string> FilePatterns => ["src/main/resources/**/*.properties", "**/*.properties"];

    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        if (!Directory.Exists(rootFolder)) yield break;
        foreach (var file in Files(rootFolder).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(rootFolder, file).Replace('\\', '/');
            yield return new DiscoveredFile(file, relative, Split(Path.GetFileNameWithoutExtension(file)).Language);
        }
    }

    public string? ExtractLanguage(string relativePath) => Split(Path.GetFileNameWithoutExtension(relativePath)).Language;

    public string GetFilePath(string rootFolder, string language, string? package = null)
    {
        var existing = Directory.Exists(rootFolder) ? Files(rootFolder).Order(StringComparer.Ordinal).FirstOrDefault() : null;
        var folder = existing is null ? Path.Combine(rootFolder, "src", "main", "resources") : Path.GetDirectoryName(existing)!;
        var baseName = existing is null ? "messages" : Split(Path.GetFileNameWithoutExtension(existing)).BaseName;
        return Path.Combine(folder, language == BaseLanguage ? baseName + ".properties" : $"{baseName}_{language.Replace('-', '_')}.properties");
    }

    public FrameworkDocumentLayout GetDocumentLayout(DiscoveredFile file)
    {
        var relative = file.RelativePath.Replace('\\', '/');
        var slash = relative.LastIndexOf('/');
        return new((slash < 0 ? "" : relative[..slash]) + "/@bundle/" + Split(Path.GetFileNameWithoutExtension(relative)).BaseName, TranslationPath.Empty);
    }

    public DiscoveredFile? DescribeDestination(string rootFolder, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.EndsWith(".properties", StringComparison.Ordinal) || normalized.Split('/').Any(p => p is "" or "." or "..")) return null;
        return new(Path.Combine(rootFolder, normalized.Replace('/', Path.DirectorySeparatorChar)), normalized, ExtractLanguage(normalized)!);
    }

    public int DetectionScore(string rootFolder)
    {
        if (!Directory.Exists(rootFolder) || !Files(rootFolder).Any()) return 0;
        var score = 6;
        if (File.Exists(Path.Combine(rootFolder, "pom.xml")) || File.Exists(Path.Combine(rootFolder, "build.gradle")) || File.Exists(Path.Combine(rootFolder, "build.gradle.kts"))) score += 10;
        return score;
    }

    /// <summary><c>messages_fr_CA</c> → (<c>messages</c>, <c>fr-CA</c>); <c>messages</c> → (<c>messages</c>, <c>default</c>).</summary>
    internal static (string BaseName, string Language) Split(string name)
    {
        var parts = name.Split('_');
        // A file named only by its locale (fr.properties, fr_CA.properties), as Toucan itself creates them.
        if (LanguageCode().IsMatch(parts[0]) && parts.Skip(1).All(p => ScriptOrRegion().IsMatch(p))) return ("", string.Join('-', parts));
        for (var i = 1; i < parts.Length; i++)
        {
            if (!LanguageCode().IsMatch(parts[i])) continue;
            if (parts.Skip(i + 1).All(p => ScriptOrRegion().IsMatch(p)))
                return (string.Join('_', parts.Take(i)), string.Join('-', parts.Skip(i)));
        }
        return (name, BaseLanguage);
    }

    private static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir, "*.properties")) yield return file;
            foreach (var child in Directory.EnumerateDirectories(dir))
                if (!s_skipped.Contains(Path.GetFileName(child), StringComparer.Ordinal)) pending.Push(child);
        }
    }

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageCode();

    [GeneratedRegex("^([A-Z][a-z]{3}|[A-Z]{2}|[0-9]{3})$")]
    private static partial Regex ScriptOrRegion();
}
