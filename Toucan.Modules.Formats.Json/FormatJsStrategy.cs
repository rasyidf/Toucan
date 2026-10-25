using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Public FormatJS extraction schema; generated source catalogs are observed, never rewritten.</summary>
public sealed class FormatJsStrategy : ILoadStrategy, IResourceLoadStrategy, IResourceDiscoveryStrategy, ISaveStrategy, IResourceSaveStrategy, IResourceCapabilities
{
    public const string Id = "formatjs";
    public string FormatId => Id;
    public FormatSupport Support { get; } = new(FormatEditing.Limited,
        "FormatJS extraction descriptor objects with string defaultMessage; generated source only",
        ["Literal IDs", "descriptions including objects", "unknown descriptor fields", "original source bytes"],
        ["Generated catalogs cannot be edited or created by export", "Compiled AST defaultMessage arrays are unsupported"]);
    public string DisplayName => "FormatJS extraction catalog (generated)";
    public string DefaultFilePath(string language) => "extracted/" + language + ".json";
    public bool StoresCommentsInline => true;
    public ResourcePermissions Permissions(ResourceDescriptor resource) => ResourcePermissions.Generated;
    public IEnumerable<ResourceDescriptor> DiscoverResources(string projectRoot) => Directory.Exists(Path.Combine(projectRoot, "extracted"))
        ? Directory.EnumerateFiles(Path.Combine(projectRoot, "extracted"), "*.json").Select(f => new ResourceDescriptor
        { Id = "extracted/" + Path.GetFileName(f), RelativePath = "extracted/" + Path.GetFileName(f), Language = Path.GetFileNameWithoutExtension(f), FormatId = Id,
            Dialect = "formatjs-extract", Ownership = ResourceOwnership.GeneratedSource }) : [];
    public IEnumerable<TranslationItem> Load(string folder) => DiscoverResources(folder).SelectMany(r => ReadResource(new(folder, r)).Entries);
    public ResourceReadResult ReadResource(ResourceReadRequest request)
    {
        var resource = request.Resource with { Ownership = ResourceOwnership.GeneratedSource, Dialect = "formatjs-extract" };
        var text = ResourceText.Read(request, out var encoding);
        using var json = JsonDocument.Parse(text);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("FormatJS catalog must be an ID-to-descriptor object.");
        var items = new List<TranslationItem>();
        foreach (var property in json.RootElement.EnumerateObject())
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("defaultMessage", out var message) || message.ValueKind != JsonValueKind.String)
                throw new FormatException($"FormatJS ID '{property.Name}' requires a string defaultMessage. Compiled AST catalogs are unsupported.");
            var comment = property.Value.TryGetProperty("description", out var description) ? description.ValueKind == JsonValueKind.String ? description.GetString()! : description.GetRawText() : "";
            items.Add(new() { Namespace = property.Name, Language = resource.Language, Value = message.GetString()!, Comment = comment });
        }
        var result = ResourceFileAdapter.Bind(request with { Resource = resource }, items, literal: true);
        result.Document!.Data["formatjs.original"] = json.RootElement.Clone();
        result.Document.DetectedOptions = ResourceText.Detect(text, encoding);
        return result;
    }
    public ResourceWriteResult WriteResource(ResourceWriteRequest request)
    {
        var original = request.Document.Data["formatjs.original"];
        var entries = ResourceFileAdapter.Entries(request);
        var source = original.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        if (entries.Count != source.Count || entries.Any(i => !source.TryGetValue(i.Namespace, out var descriptor) || descriptor.GetProperty("defaultMessage").GetString() != i.Value ||
            i.Comment != (descriptor.TryGetProperty("description", out var d) ? d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText() : "")))
            throw new InvalidOperationException("Generated FormatJS source cannot be edited. Change source code and extract again.");
        // No write, even on Save As: the existing save-copy path copies original files first.
        return new([]);
    }
    public void Save(string path, SaveContext context)
    {
        if (context.Documents.Count == 0) throw new InvalidOperationException("Generated catalogs cannot be created by translation export. Use the framework extractor.");
        foreach (var doc in context.Documents) WriteResource(new(path, doc, context.LanguageDictionary.Values.SelectMany(v => v).Where(i => i.Identity?.ResourceId == doc.Resource.Id).ToList()));
    }
    public Task SaveAsync(string path, SaveContext context) { Save(path, context); return Task.CompletedTask; }
}
