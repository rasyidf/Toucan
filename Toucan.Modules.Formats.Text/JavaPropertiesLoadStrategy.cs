using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class JavaPropertiesLoadStrategy : ILoadStrategy, IResourceLoadStrategy
{
    public string FormatId => FormatIds.JavaProperties;

    public IEnumerable<TranslationItem> Load(string folder) => Load(folder, null);

    public IEnumerable<TranslationItem> Load(string folder, string? textEncoding)
    {
        var files = Directory.GetFiles(folder, "*.properties", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            // Handle messages_en.properties pattern
            var underscoreIdx = lang.LastIndexOf('_');
            if (underscoreIdx > 0)
                lang = lang[(underscoreIdx + 1)..];

            items.AddRange(ReadFile(file, lang, Path.GetRelativePath(folder, file), textEncoding, out _, out _));
        }
        return items;
    }

    public ResourceReadResult ReadResource(ResourceReadRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Resource.RelativePath);
        var encoding = ResourceOptions.Resolve(request.Resource.Options, request.ProjectOptions, null, null).InputEncoding;
        var items = ReadFile(file, request.Resource.Language, request.Resource.RelativePath, encoding, out var text, out var detected);
        var result = ResourceFileAdapter.Bind(request, items, true, new() { ["properties.encoding"] = detected });
        result.Document!.Data["properties"] = System.Text.Json.JsonSerializer.SerializeToElement(text);
        result.Document.DetectedOptions = ResourceText.Detect(text, detected);
        return result;
    }

    private static List<TranslationItem> ReadFile(string file, string lang, string relative, string? textEncoding, out string original, out string detected)
    {
        var items = new List<TranslationItem>();
        var bytes = File.ReadAllBytes(file);
        if (textEncoding != null) ResourceText.EncodingFor(textEncoding, relative, "inputEncoding", latin: true);
        detected = "ISO-8859-1";
        if (textEncoding is "ISO-8859-1" or "Latin1") original = Encoding.Latin1.GetString(bytes);
        else if (textEncoding is "UTF-8" or "UTF-8 BOM")
        {
            original = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            detected = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? "UTF-8 BOM" : "UTF-8";
        }
        else try
        {
            original = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            if (bytes.Any(b => b >= 128)) detected = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? "UTF-8 BOM" : "UTF-8";
        }
        catch (DecoderFallbackException) { original = Encoding.Latin1.GetString(bytes); }
        foreach (var line in PropertiesDocumentCodec.Parse(original).Where(l => l.Key != null))
            items.Add(new TranslationItem { Language = lang, Namespace = line.Key!, Value = line.Value ?? "",
                FormatData = new() { ["properties.encoding"] = detected, ["properties.file"] = relative } });

        return items;
    }

}
