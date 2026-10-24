using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>
/// Loads Flutter ARB (Application Resource Bundle) JSON files. The <c>@key</c> metadata object of each
/// message and the other <c>@@</c> header entries go in <see cref="TranslationItem.FormatData"/> under the
/// <see cref="ArbFormat"/> keys so the save strategy can write them back.
/// </summary>
public class ArbLoadStrategy : ILoadStrategy, IResourceLoadStrategy, IResourceMetadataAuthority
{
    private static readonly Regex s_scriptOrRegion = new("^([A-Z][a-z]{3}|[A-Z]{2}|[0-9]{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_languageCode = new("^[a-z]{2,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool CommentsInResource => true;
    public bool OwnsMetadata(string key) => key.StartsWith("arb.", StringComparison.Ordinal);

    public string FormatId => FormatIds.Arb;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.arb", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(content);

                // @@locale wins; otherwise the locale comes from the file name (app_en.arb → en, app_en_US.arb → en_US)
                var lang = "unknown";
                if (doc.RootElement.TryGetProperty("@@locale", out var localeProp))
                    lang = localeProp.GetString() ?? "unknown";
                else
                    lang = LocaleFromFileName(Path.GetFileNameWithoutExtension(file)) ?? lang;

                var relative = Path.GetRelativePath(folder, file);
                var header = new Dictionary<string, JsonElement>();
                var metadata = new Dictionary<string, string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.StartsWith("@@", StringComparison.Ordinal))
                    {
                        if (prop.Name != "@@locale") header[prop.Name] = prop.Value;
                    }
                    else if (prop.Name.StartsWith('@'))
                        metadata[prop.Name[1..]] = prop.Value.GetRawText();
                }
                var headerJson = header.Count == 0 ? null : JsonSerializer.Serialize(header);

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.StartsWith('@')) continue;
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;

                    var data = new Dictionary<string, string> { [ArbFormat.File] = relative };
                    if (metadata.TryGetValue(prop.Name, out var meta)) data[ArbFormat.Metadata] = meta;
                    if (headerJson != null) data[ArbFormat.Header] = headerJson;

                    var item = new TranslationItem { Language = lang, Namespace = prop.Name, Value = prop.Value.GetString() ?? "", FormatData = data };
                    ApplyMetadata(item, data.GetValueOrDefault(ArbFormat.Metadata));
                    items.Add(item);
                }
            }
            catch { }
        }
        return items;
    }

    public ResourceReadResult ReadResource(ResourceReadRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Resource.RelativePath);
        var content = ResourceText.Read(request, out var encoding);
        using var json = JsonDocument.Parse(content);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException($"{request.Resource.RelativePath} [root]: ARB must be an object.");
        var items = new List<TranslationItem>();
        foreach (var prop in json.RootElement.EnumerateObject())
        {
            if (prop.Name.StartsWith('@')) continue;
            if (prop.Value.ValueKind != JsonValueKind.String) throw new FormatException($"{request.Resource.RelativePath} [{prop.Name}]: ARB message must be a string.");
            var item = new TranslationItem { Language = request.Resource.Language, Namespace = prop.Name, Value = prop.Value.GetString() ?? "",
                FormatData = new() { [ArbFormat.File] = request.Resource.RelativePath } };
            if (json.RootElement.TryGetProperty("@" + prop.Name, out var metadata)) item.FormatData[ArbFormat.Metadata] = metadata.GetRawText();
            ApplyMetadata(item, item.FormatData.GetValueOrDefault(ArbFormat.Metadata));
            items.Add(item);
        }
        var result = ResourceFileAdapter.Bind(request, items, true);
        result.Document!.DetectedOptions = ResourceText.Detect(content, encoding);
        result.Document.Data["arb"] = json.RootElement.Clone();
        return result;
    }

    private static void ApplyMetadata(TranslationItem item, string? raw)
    {
        if (raw == null) return;
        using var json = JsonDocument.Parse(raw);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("ARB message metadata must be an object.");
        if (json.RootElement.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
            item.Comment = description.GetString() ?? "";
        if (json.RootElement.TryGetProperty("placeholders", out var placeholders))
        {
            if (placeholders.ValueKind != JsonValueKind.Object) throw new FormatException("ARB placeholders must be an object.");
            (item.FormatData ??= new())[ArbFormat.Placeholders] = placeholders.GetRawText();
        }
    }

    /// <summary>
    /// Finds the locale in <c>app_en</c>, <c>app_en_US</c> or <c>intl_zh_Hans_CN</c>: the first part that is a
    /// language code and is followed only by script/region parts. Falls back to the last part.
    /// </summary>
    internal static string? LocaleFromFileName(string name)
    {
        var parts = name.Split('_');
        if (parts.Length < 2) return null;

        for (var i = 1; i < parts.Length; i++)
        {
            if (!s_languageCode.IsMatch(parts[i])) continue;
            if (parts.Skip(i + 1).All(p => s_scriptOrRegion.IsMatch(p))) return string.Join('_', parts.Skip(i));
        }
        return parts[^1];
    }
}

/// <summary>Keys used in <see cref="TranslationItem.FormatData"/> for ARB entries.</summary>
public static class ArbFormat
{
    public const string File = "arb.file";
    public const string Placeholders = "arb.placeholders";
    /// <summary>The raw JSON of the message's <c>@key</c> object.</summary>
    public const string Metadata = "arb.metadata";
    /// <summary>The raw JSON of the file's <c>@@</c> entries other than <c>@@locale</c>; repeated on every item of the file so it survives when some are empty.</summary>
    public const string Header = "arb.header";
}
