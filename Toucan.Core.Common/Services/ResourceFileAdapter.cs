using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Shared identity and validation for file adapters; parsing stays with each format.</summary>
public static class ResourceFileAdapter
{
    public static ResourceReadResult Bind(ResourceReadRequest request, List<TranslationItem> items, bool literal,
        Dictionary<string, string>? metadata = null)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var resource = request.Resource;
        foreach (var item in items)
        {
            var key = item.Namespace;
            var path = literal ? new[] { ResourcePathSegment.Named(key) } : ResourceEntryOperations.ParseSelector(key).ToArray();
            item.Identity = new ResourceEntryIdentity
            {
                EntryId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource.Id + "\0" + JsonSerializer.Serialize(path)))),
                ResourceId = resource.Id, Package = resource.Package, PackagePrefix = resource.SelectorPrefix,
                Path = path, Selector = resource.SelectorPrefix + key
            };
            item.Namespace = item.Identity.Selector;
            item.Language = resource.Language;
        }
        if (items.Select(i => i.Identity!.KeyToken).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new FormatException($"{resource.RelativePath} [key]: duplicate resource keys.");
        var document = new ResourceDocumentState { Resource = resource };
        if (metadata is not null) document.Data["format"] = JsonSerializer.SerializeToElement(metadata);
        return new ResourceReadResult(items, document, []);
    }

    public static List<TranslationItem> Entries(ResourceWriteRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        var resource = request.Document.Resource;
        var items = request.Entries.Select(i =>
        {
            if (i.Identity is null || i.Identity.ResourceId != resource.Id || i.Language != resource.Language)
                throw new InvalidOperationException($"{resource.RelativePath} [identity]: entry belongs to a different resource.");
            var copy = ResourceEntryOperations.Copy(i);
            var key = i.Identity.Path.Count == 1 && i.Identity.Path[0].Property is { } literal
                ? literal : ResourceEntryOperations.Selector(i.Identity.Path);
            copy.Identity = null;
            copy.Namespace = key;
            return copy;
        }).ToList();
        if (items.Select(i => i.Namespace).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new InvalidOperationException($"{resource.RelativePath} [key]: duplicate output keys.");
        return items;
    }

    public static Dictionary<string, string> Metadata(ResourceDocumentState document) =>
        document.Data.TryGetValue("format", out var data) ? data.Deserialize<Dictionary<string, string>>() ?? [] : [];

    public static bool KeepEmpty(ResourceWriteRequest request) =>
        ResourceOptions.Resolve(request.Document.Resource.Options, request.ProjectOptions, request.Document.DetectedOptions, null).SaveEmptyTranslations ?? true;
}
