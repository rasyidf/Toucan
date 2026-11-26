using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services.LoadStrategies;

namespace Toucan.Core.Services.SaveStrategies;

/// <summary>
/// Document saving for ARB. Comments are the messages' <c>@key.description</c> fields, kept in the file (not in sidecars), so
/// Flutter's gen-l10n keeps them as doc comments; placeholders and unknown <c>@key</c> fields travel as format data. Changed values
/// and changed descriptions replace only their own tokens. Renames, insertions, deletions, added or removed descriptions and changed
/// placeholders rebuild the object in its original order, keeping each <c>@key</c> block with its message.
/// </summary>
public partial class ArbSaveStrategy : IDocumentSaveStrategy, IResourceMetadataAuthority
{
    public bool CommentsInResource => true;
    public bool OwnsMetadata(string key) => key.StartsWith("arb.", StringComparison.Ordinal);

    public string SerializeDocument(string originalContent, IReadOnlyList<DocumentTranslationEdit> edits, CancellationToken cancellationToken = default) =>
        SerializeDocument(originalContent, edits, new ResourceOptions(), cancellationToken);

    public string SerializeDocument(string originalContent, IReadOnlyList<DocumentTranslationEdit> edits, ResourceOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalContent);
        ArgumentNullException.ThrowIfNull(edits);
        cancellationToken.ThrowIfCancellationRequested();
        options = options with
        {
            UnicodeEscapes = options.UnicodeEscapes is null or ResourceUnicodeEscapes.Preserve ? ResourceText.DetectJsonEscapes(originalContent) : options.UnicodeEscapes,
        };
        if (JsonNode.Parse(originalContent) is not JsonObject root) throw new JsonException("An ARB document must have an object root.");
        Validate(root, edits);
        if (edits.Count == 0) return originalContent;

        // Token edits when nothing moves, no @key block changes beyond its description, and every description change replaces
        // an existing description with text.
        var tokens = new List<DocumentTranslationEdit>();
        var structural = false;
        foreach (var edit in edits)
        {
            if (edit.OriginalPath is null || edit.Translation is null || !edit.OriginalPath.Equals(edit.Translation.KeyPath)) { structural = true; break; }
            var key = edit.OriginalPath.Segments[0];
            var existing = root["@" + key] as JsonObject;
            var desired = DesiredMetadata(edit.Translation, existing);
            if (!JsonNode.DeepEquals(WithoutDescription(existing), WithoutDescription(desired))) { structural = true; break; }
            if (root[key]?.GetValue<string>() != edit.Translation.Value) tokens.Add(new(edit.OriginalPath, edit.Translation with { Comment = null, FormatData = null }));
            var before = Description(existing);
            var after = Description(desired);
            if (before == after) continue;
            if (before is null || after is null) { structural = true; break; }
            TranslationPath path = new(["@" + key, "description"]);
            tokens.Add(new(path, new DocumentTranslation(path, after)));
        }
        if (!structural)
            return tokens.Count == 0 ? originalContent : JsonDocumentSerializer.Serialize(originalContent, tokens, cancellationToken, options);
        return JsonDocumentSerializer.Format(Rebuild(root, edits, cancellationToken), originalContent, options);
    }

    /// <summary>
    /// The <c>@key</c> block a translation should have: the block it already has (or, for a new message, the one it carries), with its
    /// description from the comment and its placeholders from the format data. Null when the block would be empty.
    /// </summary>
    private static JsonObject? DesiredMetadata(DocumentTranslation translation, JsonObject? existing)
    {
        var data = translation.FormatData;
        var meta = existing?.DeepClone() as JsonObject
            ?? (data?.GetValueOrDefault(ArbFormat.Metadata) is { } raw ? JsonNode.Parse(raw) as JsonObject ?? throw new JsonException("ARB metadata must be an object.") : null)
            ?? new JsonObject();
        if (translation.Comment is { Length: > 0 } comment) meta["description"] = comment;
        else if (translation.Comment is not null) meta.Remove("description");
        if (data?.GetValueOrDefault(ArbFormat.Placeholders) is { } placeholders)
        {
            var parsed = JsonNode.Parse(placeholders) as JsonObject ?? throw new JsonException("ARB placeholders must be an object.");
            // An empty editor result does not add an empty "placeholders" object to a message that had none.
            if (parsed.Count > 0 || meta.ContainsKey("placeholders")) meta["placeholders"] = parsed;
        }
        // A block that became empty goes; one that was already empty in the file stays as it was.
        return meta.Count == 0 && existing is not { Count: 0 } ? null : meta;
    }

    private static JsonObject? WithoutDescription(JsonObject? meta)
    {
        if (meta is null) return null;
        var copy = (JsonObject)meta.DeepClone();
        copy.Remove("description");
        return copy.Count == 0 ? null : copy;
    }

    private static void Validate(JsonObject root, IReadOnlyList<DocumentTranslationEdit> edits)
    {
        var originals = new HashSet<string>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            if (edit.OriginalPath is null && edit.Translation is null) throw new ArgumentException("An edit must remove or write a translation.", nameof(edits));
            if (edit.OriginalPath is { } original)
            {
                if (original.Segments.Count != 1 || original.Segments[0].StartsWith('@') || root[original.Segments[0]] is not JsonValue value
                    || value.GetValueKind() != JsonValueKind.String || !originals.Add(original.Segments[0]))
                    throw new InvalidOperationException($"Original translation '{original.EscapedPath}' is missing or repeated.");
            }
            if (edit.Translation is { } translation
                && (translation.KeyPath.Segments.Count != 1 || translation.KeyPath.Segments[0].Length == 0 || translation.KeyPath.Segments[0].StartsWith('@')
                    || !destinations.Add(translation.KeyPath.Segments[0])))
                throw new InvalidOperationException("ARB messages are top-level keys that do not start with '@', and destinations must be unique.");
        }
        foreach (var destination in destinations)
            if (root.ContainsKey(destination) && !originals.Contains(destination))
                throw new InvalidOperationException($"Destination '{destination}' already exists.");
    }

    private static JsonObject Rebuild(JsonObject root, IReadOnlyList<DocumentTranslationEdit> edits, CancellationToken ct)
    {
        var byOriginal = edits.Where(e => e.OriginalPath is not null).ToDictionary(e => e.OriginalPath!.Segments[0], StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var result = new JsonObject();
        void Write(DocumentTranslation translation, JsonObject? existing)
        {
            var key = translation.KeyPath.Segments[0];
            result[key] = JsonValue.Create(translation.Value);
            if (DesiredMetadata(translation, existing) is { } meta) result["@" + key] = meta;
            written.Add("@" + key);
        }
        foreach (var (name, node) in root)
        {
            ct.ThrowIfCancellationRequested();
            if (name.StartsWith("@@", StringComparison.Ordinal)) result[name] = node?.DeepClone();
            else if (name.StartsWith('@'))
            {
                // A message's block travels with its message; other blocks stay where they are.
                if (!byOriginal.ContainsKey(name[1..]) && !written.Contains(name)) result[name] = node?.DeepClone();
            }
            else if (byOriginal.TryGetValue(name, out var edit))
            {
                if (edit.Translation is { } translation) Write(translation, root["@" + name] as JsonObject);
            }
            else result[name] = node?.DeepClone();
        }
        foreach (var edit in edits.Where(e => e.OriginalPath is null)) Write(edit.Translation!, null);
        return result;
    }

    private static string? Description(JsonObject? meta) =>
        meta?["description"] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
