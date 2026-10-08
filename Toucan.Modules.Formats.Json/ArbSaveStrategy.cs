using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class ArbSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Arb;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "Flutter ARB (intl_*.arb, app_*.arb), region and script locales",
        ["@key metadata (description, placeholders)", "@@ header entries", "ICU plural and select messages kept as text", "@@locale"],
        ["ICU messages are edited as one string, not as separate plural forms"]);
    public string DisplayName => "Flutter ARB";
    public IReadOnlyList<string> FileExtensions => [".arb"];
    public string DefaultFilePath(string language) => $"app_{language}.arb";

    public FormatDetection Detection { get; } = new(0, [".arb"], []);

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        var root = Path.GetFullPath(path);
        foreach (var (language, list) in context.LanguageDictionary)
        {
            // Entries go back to the file they were loaded from; new ones go to app_<language>.arb.
            foreach (var group in list.NoEmpty().GroupBy(i => FileFor(i, language, root)))
                WriteFile(root, group.Key, language, group.OrderBy(i => i.Namespace, StringComparer.Ordinal).ToList());
        }
    }

    private string FileFor(TranslationItem item, string language, string root)
    {
        if (item.FormatData?.TryGetValue(ArbFormat.File, out var rel) == true && !string.IsNullOrEmpty(rel))
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return rel;
        }
        return DefaultFilePath(language);
    }

    private static void WriteFile(string root, string relative, string language, List<TranslationItem> items)
    {
        var obj = new JsonObject { ["@@locale"] = language };

        var header = items.Select(i => i.FormatData?.GetValueOrDefault(ArbFormat.Header)).FirstOrDefault(h => h != null);
        if (header != null && JsonNode.Parse(header) is JsonObject extra)
            foreach (var (key, value) in extra.ToList())
            {
                extra.Remove(key);
                obj[key] = value;
            }

        foreach (var item in items)
        {
            obj[item.Namespace] = item.Value ?? "";
            var meta = item.FormatData?.GetValueOrDefault(ArbFormat.Metadata);
            if (meta != null) obj["@" + item.Namespace] = JsonNode.Parse(meta);
        }

        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Toucan.Core.Services.AtomicFile.WriteAllText(full, obj.ToJsonString(s_options));
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
