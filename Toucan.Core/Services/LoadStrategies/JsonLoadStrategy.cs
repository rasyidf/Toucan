using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public partial class JsonLoadStrategy(IFileService fileService, ILogger<JsonLoadStrategy> logger) : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Json;

    private static readonly HashSet<string> s_excludedDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".toucan", ".git", "node_modules", ".next", "dist", "build", "out", "obj", "bin", ".idea", ".vscode" };

    private static readonly HashSet<string> s_excludedFiles = new(StringComparer.OrdinalIgnoreCase)
        { "toucan.project", "package.json", "package-lock.json", "tsconfig.json", "global.json", "appsettings.json" };

    public IEnumerable<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var files = EnumerateJsonFiles(folder);
        var items = new List<TranslationItem>();

        foreach (var filePath in files)
        {
            var filename = Path.GetFileName(filePath);
            var (lang, prefix) = GetLanguageAndPrefix(folder, filePath);
            if (string.IsNullOrEmpty(lang)) continue; // Skip files where language can't be determined
            var folderOfFile = Path.GetDirectoryName(filePath) ?? folder;
            var content = fileService.ReadText(folderOfFile, filename);

            if (string.IsNullOrWhiteSpace(content)) continue;

            var parsed = NestedJsonParser.ParseNestedJson(lang, content, logger);

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                foreach (var p in parsed)
                    p.Namespace = string.IsNullOrWhiteSpace(p.Namespace) ? prefix : $"{prefix}.{p.Namespace}";
            }

            items.AddRange(parsed);

            if (items.Count == 0)
                items.Add(new TranslationItem { Language = lang });
        }

        return items;
    }

    private static (string language, string prefix) GetLanguageAndPrefix(string rootFolder, string filePath)
    {
        var relative = Path.GetRelativePath(rootFolder, filePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 1)
            return (Path.GetFileNameWithoutExtension(segments[0]), string.Empty);

        var localesIdx = Array.FindIndex(segments, s => s.Equals("locales", StringComparison.OrdinalIgnoreCase));
        int languageIndex;

        if (localesIdx >= 0 && localesIdx < segments.Length - 1)
            languageIndex = localesIdx + 1;
        else if (IsLanguageCandidate(segments[0]))
            languageIndex = 0;
        else if (segments.Length >= 2 && IsLanguageCandidate(segments[^2]))
            languageIndex = segments.Length - 2;
        else
            languageIndex = Math.Max(0, segments.Length - 2);

        var lang = Path.GetFileNameWithoutExtension(segments[languageIndex]);

        var prefixSegments = new List<string>();
        for (int i = languageIndex + 1; i < segments.Length; i++)
        {
            var seg = i == segments.Length - 1 ? Path.GetFileNameWithoutExtension(segments[i]) : segments[i];
            prefixSegments.Add(seg);
        }

        return (lang, prefixSegments.Count == 0 ? string.Empty : string.Join('.', prefixSegments));
    }

    private static bool IsLanguageCandidate(string segment) =>
        !string.IsNullOrWhiteSpace(segment) && LanguagePattern().IsMatch(segment);

    private static IEnumerable<string> EnumerateJsonFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (!s_excludedDirs.Contains(name))
                    stack.Push(sub);
            }
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                if (!s_excludedFiles.Contains(fileName))
                    yield return file;
            }
        }
    }

    [GeneratedRegex(@"^[a-z]{2}(-[A-Za-z0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguagePattern();
}
