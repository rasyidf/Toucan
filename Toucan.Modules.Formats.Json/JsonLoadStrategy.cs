using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public partial class JsonLoadStrategy(IFileService fileService, ILogger<JsonLoadStrategy> logger) : ILoadStrategy, IResourceLoadStrategy, IResourceDiscoveryStrategy
{
    public string FormatId => FormatIds.Json;

    private static readonly HashSet<string> s_excludedFiles = new(StringComparer.OrdinalIgnoreCase)
        { "toucan.tproj", "package.json", "package-lock.json", "tsconfig.json", "global.json", "appsettings.json" };

    public IEnumerable<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var files = FileEnumerator.EnumerateFiles(
            folder,
            "*.json",
            EnumerateOptions.SkipNestedLocaleDirs,
            s_excludedFiles);

        var items = new List<TranslationItem>();

        foreach (var filePath in files)
        {
            var filename = Path.GetFileName(filePath);
            var (lang, prefix) = GetLanguageAndPrefix(folder, filePath);
            if (string.IsNullOrEmpty(lang)) continue; // Skip files where language can't be determined
            var folderOfFile = Path.GetDirectoryName(filePath) ?? folder;
            var content = fileService.ReadText(folderOfFile, filename);

            if (string.IsNullOrWhiteSpace(content)) continue;

            var resource = Describe(folder, filePath, lang, prefix);
            var parsed = ResourceJsonCodec.Read(content, resource).Entries.ToList();
            foreach (var item in parsed) (item.FormatData ??= new())["json.file"] = resource.RelativePath;

            items.AddRange(parsed);

            if (items.Count == 0)
                items.Add(new TranslationItem { Language = lang });
        }

        return items;
    }

    public IEnumerable<ResourceDescriptor> DiscoverResources(string projectRoot)
    {
        foreach (var file in FileEnumerator.EnumerateFiles(projectRoot, "*.json", EnumerateOptions.SkipNestedLocaleDirs, s_excludedFiles))
        {
            var (language, prefix) = GetLanguageAndPrefix(projectRoot, file);
            if (!string.IsNullOrEmpty(language)) yield return Describe(projectRoot, file, language, prefix);
        }
    }

    private static ResourceDescriptor Describe(string root, string file, string language, string prefix) => new()
    {
        Id = Path.GetRelativePath(root, file).Replace('\\', '/'), RelativePath = Path.GetRelativePath(root, file).Replace('\\', '/'),
        Language = language, FormatId = FormatIds.Json, Package = string.IsNullOrEmpty(prefix) ? "main" : prefix,
        SelectorPrefix = string.IsNullOrEmpty(prefix) ? "" : prefix + "."
    };

    public ResourceReadResult ReadResource(ResourceReadRequest request)
    {
        if (logger.IsEnabled(LogLevel.Debug)) logger.LogDebug("Reading explicit JSON resource {Resource}", request.Resource.RelativePath);
        request.CancellationToken.ThrowIfCancellationRequested();
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Resource.RelativePath);
        var options = ResourceOptions.Resolve(request.Resource.Options, request.ProjectOptions, null, new ResourceOptions { InputEncoding = "UTF-8" });
        return ResourceJsonCodec.Read(File.ReadAllText(file, System.Text.Encoding.GetEncoding(options.InputEncoding!)), request.Resource, request.CancellationToken);
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

    [GeneratedRegex(@"^[a-z]{2}(-[A-Za-z0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguagePattern();
}
