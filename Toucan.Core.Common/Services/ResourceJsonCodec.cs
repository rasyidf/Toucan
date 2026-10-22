using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>JSON document state and typed paths shared by folder and exact-file entry points.</summary>
public static class ResourceJsonCodec
{
    public static ResourceReadResult Read(string content, ResourceDescriptor resource, CancellationToken cancellationToken = default)
    {
        using var json = JsonDocument.Parse(content);
        if (json.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new FormatException("A translation JSON document must have an object or array root.");
        var entries = new List<TranslationItem>();
        Walk(json.RootElement, []);
        ResolveSelectors(entries);
        return new ResourceReadResult(entries, new ResourceDocumentState
        {
            Resource = resource, Data = new() { ["json"] = json.RootElement.Clone() },
            DetectedOptions = new ResourceOptions
            {
                Newline = content.Contains("\r\n", StringComparison.Ordinal) ? ResourceNewline.CrLf : ResourceNewline.Lf,
                Indent = content.Contains("\n\t", StringComparison.Ordinal) ? ResourceIndent.Tab : content.Contains("\n    ", StringComparison.Ordinal)
                    ? ResourceIndent.Space4 : content.Contains('\n') ? ResourceIndent.Space2 : null
            }
        }, []);

        void Walk(JsonElement node, ResourcePathSegment[] path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject()) Walk(property.Value, [.. path, ResourcePathSegment.Named(property.Name)]);
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var child in node.EnumerateArray()) Walk(child, [.. path, ResourcePathSegment.At(index++)]);
            }
            else if (node.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                var selector = resource.SelectorPrefix + ResourceEntryOperations.Selector(path);
                entries.Add(new TranslationItem
                {
                    Language = resource.Language, Namespace = selector, Value = node.ValueKind == JsonValueKind.String ? node.GetString()! : node.ToString(),
                    Identity = new ResourceEntryIdentity
                    {
                        EntryId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resource.Id + "\0" + JsonSerializer.Serialize(path)))),
                        ResourceId = resource.Id, Package = resource.Package, Path = path, Selector = selector, PackagePrefix = resource.SelectorPrefix
                    },
                    FormatData = node.ValueKind == JsonValueKind.String ? null : new() { [NestedJsonParser.JsonScalarKey] = node.GetRawText() }
                });
            }
        }
    }

    public static void ResolveSelectors(IReadOnlyList<TranslationItem> entries)
    {
        foreach (var item in entries.Where(i => string.IsNullOrWhiteSpace(i.Namespace) && i.Identity is not null))
        {
            var selector = "~key:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(item.Identity!.KeyToken));
            item.Identity = item.Identity with { Selector = selector };
            item.Namespace = selector;
        }
        foreach (var group in entries.GroupBy(i => i.Namespace).Where(g => g.Select(i => i.Identity?.KeyToken).Distinct().Count() > 1))
            foreach (var item in group)
            {
                var identity = item.Identity!;
                var selector = identity.PackagePrefix + ResourceEntryOperations.Selector(identity.Path, escaped: true);
                item.Identity = identity with { Selector = selector };
                item.Namespace = selector;
            }
        // Escape reserved characters even without a dotted-key collision. Then resolve any
        // remaining collisions across documents/packages without relying on display text.
        foreach (var item in entries.Where(i => i.Identity?.Path.Any(p => p.Property?.IndexOfAny(['\\', '[', ']']) >= 0) == true))
        {
            var identity = item.Identity!;
            var selector = identity.PackagePrefix + ResourceEntryOperations.Selector(identity.Path, escaped: true);
            item.Identity = identity with { Selector = selector };
            item.Namespace = selector;
        }
        foreach (var group in entries.GroupBy(i => i.Namespace).Where(g => g.Select(i => i.Identity is { } id ? id.Package + "\0" + id.KeyToken : "").Distinct().Count() > 1).ToList())
            foreach (var item in group)
            {
                var identity = item.Identity!;
                var selector = "~" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { identity.Package, identity.Path })));
                while (entries.Any(i => !ReferenceEquals(i, item) && i.Language == item.Language && i.Namespace == selector)) selector = "~" + selector;
                item.Identity = identity with { Selector = selector };
                item.Namespace = selector;
            }
    }

    public static string Write(ResourceDocumentState document, IReadOnlyList<TranslationItem> entries, ResourceOptions? projectOptions = null)
    {
        var options = ResourceOptions.Resolve(document.Resource.Options, projectOptions, document.DetectedOptions,
            new ResourceOptions { SaveEmptyTranslations = true, Indent = ResourceIndent.Space2, Order = ResourceOrder.Alphabetical });
        JsonNode root = document.Data.TryGetValue("json", out var original)
            ? JsonNode.Parse(original.GetRawText())! : new JsonObject();
        ClearEditable(root);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<TranslationItem> ordered = options.Order == ResourceOrder.Alphabetical ? entries.OrderBy(i => i.Namespace, StringComparer.Ordinal) : entries;
        foreach (var item in ordered)
        {
            if (options.SaveEmptyTranslations == false && string.IsNullOrEmpty(item.Value)) continue;
            var path = item.Identity?.Path ?? ResourceEntryOperations.ParseSelector(item.Namespace);
            if (path.Count == 0 || path.Any(p => (p.Property is null) == (p.Index is null) || p.Index < 0))
                throw new InvalidOperationException("A resource key needs property or array-index segments.");
            var token = JsonSerializer.Serialize(path);
            if (!keys.Add(token)) throw new InvalidOperationException("Two translations have the same resource address.");
            JsonNode? value = JsonValue.Create(item.Value);
            if (item.FormatData?.GetValueOrDefault(NestedJsonParser.JsonScalarKey) is { } raw)
            {
                using var scalar = JsonDocument.Parse(raw);
                if (scalar.RootElement.ToString() == item.Value) value = JsonNode.Parse(raw);
            }
            Set(root, path, value);
        }
        if (options.Order == ResourceOrder.Alphabetical) root = Sort(root)!;
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = options.Indent != ResourceIndent.None,
            IndentCharacter = options.Indent == ResourceIndent.Tab ? '\t' : ' ',
            IndentSize = options.Indent == ResourceIndent.Space4 ? 4 : options.Indent == ResourceIndent.Tab ? 1 : 2
        };
        var text = root.ToJsonString(jsonOptions);
        return options.Newline == ResourceNewline.CrLf ? text.ReplaceLineEndings("\r\n") : text.ReplaceLineEndings("\n");
    }

    private static bool ClearEditable(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var wasEmpty = obj.Count == 0;
            foreach (var (key, child) in obj.ToArray())
                if (child is JsonValue) obj.Remove(key);
                else if (child is not null && ClearEditable(child)) obj.Remove(key);
            return !wasEmpty && obj.Count == 0;
        }
        if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue) array[i] = null;
                else if (array[i] is { } child) ClearEditable(child);
        return false;
    }

    private static void Set(JsonNode root, IReadOnlyList<ResourcePathSegment> path, JsonNode? value)
    {
        var current = root;
        for (var i = 0; i < path.Count; i++)
        {
            var part = path[i];
            var last = i == path.Count - 1;
            JsonNode NewContainer() => path[i + 1].Index is not null ? new JsonArray() : new JsonObject();
            if (part.Index is { } index && current is JsonArray array)
            {
                while (array.Count <= index) array.Add(null);
                if (last) array[index] = value;
                else { array[index] ??= NewContainer(); current = array[index]!; }
            }
            else if (part.Property is { } name && current is JsonObject obj)
            {
                if (last) obj[name] = value;
                else { obj[name] ??= NewContainer(); current = obj[name]!; }
            }
            else throw new InvalidOperationException("A resource path conflicts with an existing object or array.");
        }
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        null => null,
        _ => node.DeepClone()
    };
}
