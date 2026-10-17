using System.Globalization;
using System.Text.Json;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public partial class JsonLoadStrategy : IDocumentLoadStrategy
{
    public IReadOnlyList<DocumentTranslation> LoadDocument(string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A translation document must have an object root.");
        var units = new List<DocumentTranslation>();
        Walk(document.RootElement, [], units, cancellationToken);
        return units.AsReadOnly();
    }

    private static void Walk(JsonElement element, string[] path, List<DocumentTranslation> units, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException($"Duplicate property '{property.Name}'.");
                    Walk(property.Value, [.. path, property.Name], units, ct);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray())
                    Walk(child, [.. path, (index++).ToString(CultureInfo.InvariantCulture)], units, ct);
                break;
            case JsonValueKind.String:
                units.Add(new(new TranslationPath(path), element.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                units.Add(new(new TranslationPath(path), element.ToString(), element.GetRawText()));
                break;
            // Nulls and empty containers remain in the host's original document content.
        }
    }
}
