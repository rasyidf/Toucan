using System.IO;
using System.Text.Json;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads Flutter ARB (Application Resource Bundle) JSON files.</summary>
public class ArbLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Arb;

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

                // ARB locale is in @@locale or derived from filename (app_en.arb → en)
                var lang = "unknown";
                if (doc.RootElement.TryGetProperty("@@locale", out var localeProp))
                    lang = localeProp.GetString() ?? "unknown";
                else
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var parts = name.Split('_');
                    if (parts.Length >= 2) lang = parts[^1]; // app_en → en
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    // Skip metadata keys (@@ and @key)
                    if (prop.Name.StartsWith('@')) continue;

                    if (prop.Value.ValueKind == JsonValueKind.String)
                        items.Add(new TranslationItem { Language = lang, Namespace = prop.Name, Value = prop.Value.GetString() ?? "" });
                }
            }
            catch { }
        }
        return items;
    }
}
