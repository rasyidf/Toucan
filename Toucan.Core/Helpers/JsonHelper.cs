using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Extensions;

public class JsonParser(string language, ILogger<JsonParser>? logger = null) : IParser
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly JsonSerializerOptions s_indentedSimpleOptions = new() { WriteIndented = true };

    private string _language = language;

    public IParser SetLanguage(string language)
    {
        _language = language;
        return this;
    }

    public async IAsyncEnumerable<TranslationItem> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(content); }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "Failed to parse JSON for language {Language}", _language);
            yield break;
        }

        try
        {
            var queue = new Queue<(string Path, JsonElement Element)>();

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    queue.Enqueue((prop.Name, prop.Value));
            }

            while (queue.TryDequeue(out var item))
            {
                if (item.Element.ValueKind == JsonValueKind.Object)
                {
                    if (item.Element.EnumerateObject().Any())
                    {
                        foreach (var prop in item.Element.EnumerateObject())
                            queue.Enqueue((string.IsNullOrEmpty(item.Path) ? prop.Name : $"{item.Path}.{prop.Name}", prop.Value));
                    }
                    else
                    {
                        if (logger?.IsEnabled(LogLevel.Debug) == true)
                        {
                            logger.LogDebug("Skipped empty object: {Path}", item.Path);
                        }
                    }
                }
                else if (item.Element.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    yield return new TranslationItem
                    {
                        Namespace = item.Path,
                        Value = item.Element.ValueKind == JsonValueKind.String ? item.Element.GetString() ?? "" : item.Element.ToString(),
                        Language = _language
                    };
                }
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            doc.Dispose();
        }
    }

    public static void SaveNs(string path, List<NsTreeItem> items, List<string> languages)
    {
        foreach (string language in languages)
        {
            Dictionary<string, object> dyn = [];
            for (int i = 0; i < items?.Count; i++)
                items[i].ToJson(dyn, language);

            var json = JsonSerializer.Serialize(dyn, s_indentedOptions);
            File.WriteAllText(Path.Combine(path, language + ".json"), json);
        }
    }

    public static void Save(string path, Dictionary<string, IEnumerable<TranslationItem>> translationItems)
    {
        foreach (var kv in translationItems)
        {
            var dict = new Dictionary<string, string>();
            foreach (var setting in kv.Value.Where(x => !string.IsNullOrEmpty(x.Value)).OrderBy(o => o.Namespace))
                dict[setting.Namespace] = setting.Value;

            var json = JsonSerializer.Serialize(dict, s_indentedSimpleOptions);
            File.WriteAllText(Path.Combine(path, kv.Key + ".json"), json);
        }
    }
}
