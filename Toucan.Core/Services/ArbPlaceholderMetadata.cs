using System.Text.Json;
using System.Text.RegularExpressions;

namespace Toucan.Core.Services;

public static partial class ArbPlaceholderMetadata
{
    public static void Validate(string json)
    {
        if (json.Length > 131072) throw new FormatException("Placeholder metadata exceeds 128 KiB.");
        using var parsed = JsonDocument.Parse(json, new() { MaxDepth = 32 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Placeholders must be a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var placeholder in parsed.RootElement.EnumerateObject())
        {
            if (names.Count >= 1000 || !names.Add(placeholder.Name) || !NamePattern().IsMatch(placeholder.Name))
                throw new FormatException("Placeholder names must be unique identifiers; at most 1,000 are supported.");
            if (placeholder.Value.ValueKind != JsonValueKind.Object) throw new FormatException($"{placeholder.Name}: metadata must be an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in placeholder.Value.EnumerateObject())
            {
                if (!fields.Add(field.Name)) throw new FormatException($"{placeholder.Name}: duplicate metadata field {field.Name}.");
                if (field.Name == "type" && field.Value.ValueKind != JsonValueKind.String) throw new FormatException($"{placeholder.Name}: type must be a string.");
            }
        }
    }
    public static string Mismatches(string message, string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var documented = parsed.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var referenced = MessagePattern().Matches(message).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return string.Join("\n", referenced.Except(documented).Select(n => $"Missing metadata: {n}")
            .Concat(documented.Except(referenced).Select(n => $"Not referenced by the message: {n}")));
    }
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.NonBacktracking)] private static partial Regex NamePattern();
    [GeneratedRegex(@"\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*[,}]", RegexOptions.NonBacktracking)] private static partial Regex MessagePattern();
}
