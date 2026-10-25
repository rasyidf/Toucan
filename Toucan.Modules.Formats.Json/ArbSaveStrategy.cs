using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class ArbSaveStrategy(IFileService fileService) : ISaveStrategy, IResourceSaveStrategy, IResourceOptionsProvider
{
    public IReadOnlySet<string> SupportedOptions(ResourceDescriptor resource) => ResourceOptionFields.With("Indent");

    public string FormatId => FormatIds.Arb;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "Flutter ARB (intl_*.arb, app_*.arb), region and script locales",
        ["Editable descriptions and structured placeholders", "unknown @key metadata", "@@ header entries", "ICU plural and select messages kept as text", "@@locale"],
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
            foreach (var group in list.ForParse().GroupBy(i => FileFor(i, language, root)))
                WriteFile(root, group.Key, language, group.OrderBy(i => i.Namespace, StringComparer.Ordinal).ToList(), context.SaveEmptyTranslations);
        }
    }

    public ResourceWriteResult WriteResource(ResourceWriteRequest request)
    {
        var entries = ResourceFileAdapter.Entries(request);
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        var obj = request.Document.Data.TryGetValue("arb", out var original) ? JsonNode.Parse(original.GetRawText())!.AsObject() : new JsonObject();
        // Retain document headers even when the last message is deleted.
        foreach (var key in obj.Where(p => !p.Key.StartsWith('@') && p.Value is JsonValue).Select(p => p.Key).ToList())
        { obj.Remove(key); obj.Remove("@" + key); }
        obj["@@locale"] = request.Document.Resource.Language;
        foreach (var item in entries.Where(i => ResourceFileAdapter.KeepEmpty(request) || !string.IsNullOrEmpty(i.Value)))
        {
            obj[item.Namespace] = item.Value ?? "";
            var metadata = Metadata(item);
            if (metadata.Count > 0) obj["@" + item.Namespace] = metadata;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var options = ResourceText.Options(request);
        if (options.Order == ResourceOrder.Alphabetical) obj = new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        ResourceText.Write(request, obj.ToJsonString(ResourceText.JsonOptions(options)));
        request.Document.Data["arb"] = JsonSerializer.SerializeToElement(obj);
        return new([]);
    }

    private static JsonObject Metadata(TranslationItem item)
    {
        var metadata = item.FormatData?.GetValueOrDefault(ArbFormat.Metadata) is { } raw
            ? JsonNode.Parse(raw) as JsonObject ?? throw new FormatException("ARB metadata must be an object.") : new JsonObject();
        if (string.IsNullOrEmpty(item.Comment)) metadata.Remove("description");
        else metadata["description"] = item.Comment;
        if (item.FormatData?.GetValueOrDefault(ArbFormat.Placeholders) is { } placeholders)
            metadata["placeholders"] = JsonNode.Parse(placeholders) as JsonObject ?? throw new FormatException("ARB placeholders must be an object.");
        return metadata;
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

    private static void WriteFile(string root, string relative, string language, List<TranslationItem> items, bool keepEmpty)
    {
        var obj = new JsonObject { ["@@locale"] = language };

        var header = items.Select(i => i.FormatData?.GetValueOrDefault(ArbFormat.Header)).FirstOrDefault(h => h != null);
        if (header != null && JsonNode.Parse(header) is JsonObject extra)
            foreach (var (key, value) in extra.ToList())
            {
                extra.Remove(key);
                obj[key] = value;
            }

        foreach (var item in items.Where(i => keepEmpty || !string.IsNullOrEmpty(i.Value)))
        {
            obj[item.Namespace] = item.Value ?? "";
            var meta = Metadata(item);
            if (meta.Count > 0) obj["@" + item.Namespace] = meta;
        }

        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Toucan.Core.Services.AtomicFile.WriteAllText(full, obj.ToJsonString(s_options));
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
