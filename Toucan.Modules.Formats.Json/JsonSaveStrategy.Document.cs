using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.SaveStrategies;

public partial class JsonSaveStrategy : IDocumentSaveStrategy
{
    public string SerializeDocument(string originalContent, IReadOnlyList<DocumentTranslationEdit> edits,
        CancellationToken cancellationToken = default) => JsonDocumentSerializer.Serialize(originalContent, edits, cancellationToken);
}

internal static class JsonDocumentSerializer
{
    public static string Serialize(string content, IReadOnlyList<DocumentTranslationEdit> edits, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(edits);
        ct.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes(content);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A translation document must have an object root.");
        var spans = new Dictionary<TranslationPath, (int Start, int Length)>();
        Index(ref reader, [], spans, ct);
        if (reader.Read()) throw new JsonException("Unexpected content after the document root.");
        var originals = new HashSet<TranslationPath>();
        var destinations = new HashSet<TranslationPath>();
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            if (edit.OriginalPath is null && edit.Translation is null) throw new ArgumentException("An edit must remove or write a translation.", nameof(edits));
            if (edit.OriginalPath is { } original && (!originals.Add(original) || !spans.ContainsKey(original)))
                throw new InvalidOperationException($"Original translation '{original.EscapedPath}' is missing or repeated.");
            if (edit.Translation is { } translation && (translation.KeyPath.Segments.Count == 0 || !destinations.Add(translation.KeyPath)))
                throw new InvalidOperationException("Translation destinations must be nonempty and unique.");
        }
        if (edits.Count == 0) return content;
        // A value-only edit changes exactly its scalar token, preserving all other source text.
        if (edits.All(e => e.OriginalPath is not null && e.Translation is not null && e.OriginalPath.Equals(e.Translation.KeyPath)))
        {
            using var result = new MemoryStream(bytes.Length);
            var cursor = 0;
            foreach (var edit in edits.OrderBy(e => spans[e.OriginalPath!].Start))
            {
                ct.ThrowIfCancellationRequested();
                var (start, length) = spans[edit.OriginalPath!];
                var replacement = Encoding.UTF8.GetBytes(Scalar(edit.Translation!));
                result.Write(bytes.AsSpan(cursor, start - cursor));
                result.Write(replacement);
                cursor = start + length;
            }
            result.Write(bytes.AsSpan(cursor));
            return Encoding.UTF8.GetString(result.ToArray());
        }
        var root = JsonNode.Parse(content)!.AsObject();
        // Remove first so swaps and multiple renames are evaluated as one transaction.
        foreach (var edit in edits.Where(e => e.OriginalPath is not null))
        {
            ct.ThrowIfCancellationRequested();
            var path = edit.OriginalPath!;
            var parent = Parent(root, path, create: false);
            if (parent is not JsonObject obj)
                throw new NotSupportedException("Structural edits of array slots require an explicit array operation.");
            if (!obj.Remove(path.Segments[^1])) throw new InvalidOperationException("Original translation is missing.");
        }
        foreach (var edit in edits.Where(e => e.Translation is not null))
        {
            ct.ThrowIfCancellationRequested();
            var translation = edit.Translation!;
            var parent = Parent(root, translation.KeyPath, create: true);
            if (parent is not JsonObject obj)
                throw new NotSupportedException("Structural edits of array slots require an explicit array operation.");
            var key = translation.KeyPath.Segments[^1];
            if (obj.ContainsKey(key)) throw new InvalidOperationException($"Destination '{translation.KeyPath.EscapedPath}' already exists.");
            obj.Add(key, JsonNode.Parse(Scalar(translation)));
        }
        var whitespace = content.Split('\n').Skip(1).Select(l => new string(l.TakeWhile(c => c is ' ' or '\t').ToArray())).FirstOrDefault(s => s.Length > 0);
        var options = new JsonSerializerOptions { WriteIndented = content.Contains('\n'),
            IndentCharacter = whitespace?.Contains('\t') == true ? '\t' : ' ', IndentSize = Math.Clamp(whitespace?.Length ?? 2, 1, 127) };
        var output = root.ToJsonString(options);
        if (content.Contains("\r\n", StringComparison.Ordinal)) output = output.Replace("\n", "\r\n", StringComparison.Ordinal);
        if (content.EndsWith('\n')) output += content.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return output;
    }

    private static string Scalar(DocumentTranslation translation)
    {
        if (translation.RawScalar is { } raw)
        {
            using var scalar = JsonDocument.Parse(raw);
            if (scalar.RootElement.ValueKind is not (JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("Raw scalar metadata must contain a number or boolean.");
            // Editing a typed scalar produces a string; unchanged values retain their original raw representation.
            if (scalar.RootElement.ToString() == translation.Value) return raw;
        }
        return JsonSerializer.Serialize(translation.Value);
    }

    private static JsonNode Parent(JsonObject root, TranslationPath path, bool create)
    {
        JsonNode node = root;
        foreach (var segment in path.Segments.Take(path.Segments.Count - 1))
        {
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out var child))
                {
                    if (!create) throw new InvalidOperationException("Original parent is missing.");
                    child = new JsonObject();
                    obj.Add(segment, child);
                }
                node = child ?? throw new InvalidOperationException("A destination crosses an existing null value.");
            }
            else if (node is JsonArray array && int.TryParse(segment, out var index) && index >= 0 && index < array.Count)
                node = array[index] ?? throw new InvalidOperationException("A destination crosses an existing null array slot.");
            else throw new InvalidOperationException("A translation path crosses an existing scalar or invalid array slot.");
        }
        return node;
    }

    private static void Index(ref Utf8JsonReader reader, string[] path, Dictionary<TranslationPath, (int, int)> spans, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");
                var name = reader.GetString()!;
                if (!names.Add(name)) throw new JsonException($"Duplicate property '{name}'.");
                if (!reader.Read()) throw new JsonException("Expected a property value.");
                Index(ref reader, [.. path, name], spans, ct);
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Unterminated object.");
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                Index(ref reader, [.. path, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture)], spans, ct);
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Unterminated array.");
        }
        else if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False)
            spans.Add(new(path), (checked((int)reader.TokenStartIndex), checked((int)(reader.BytesConsumed - reader.TokenStartIndex))));
    }
}
