using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class YamlSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Yaml;

    private readonly IFileService _fileService;

    public YamlSaveStrategy(IFileService fileService)
    {
        _fileService = fileService;
    }

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var kv in context.LanguageDictionary)
        {
            var language = kv.Key;
            var list = kv.Value;
            var sb = new StringBuilder();

            // YAML header comment
            sb.AppendLine($"# Translation file for {language}");
            sb.AppendLine("---");

            // Build a nested structure from the namespaces
            var dict = new Dictionary<string, string>();
            foreach (var item in list.NoEmpty())
            {
                dict[item.Namespace] = item.Value ?? string.Empty;
            }

            // Convert flat dict to YAML format with nested keys
            WriteYamlDict(sb, dict, 0);

            _fileService.SaveText(path, language + ".yaml", sb.ToString());
        }
    }

    private static void WriteYamlDict(StringBuilder sb, Dictionary<string, string> dict, int indent)
    {
        var grouped = dict
            .GroupBy(kvp => GetRootKey(kvp.Key))
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var rootKey = group.Key;
            var items = group.ToList();

            // Check if all items are leaf nodes (no further nesting)
            var isLeaf = items.All(kvp => kvp.Key == rootKey);

            if (isLeaf && items.Count == 1)
            {
                // Simple key-value pair
                var value = EscapeYamlValue(items[0].Value);
                sb.AppendLine($"{new string(' ', indent)}{rootKey}: {value}");
            }
            else
            {
                // Nested structure
                sb.AppendLine($"{new string(' ', indent)}{rootKey}:");
                
                var nested = new Dictionary<string, string>();
                foreach (var item in items)
                {
                    var remainingKey = GetRemainingKey(item.Key, rootKey);
                    if (!string.IsNullOrEmpty(remainingKey))
                    {
                        nested[remainingKey] = item.Value;
                    }
                }

                if (nested.Count > 0)
                {
                    WriteYamlDict(sb, nested, indent + 2);
                }
            }
        }
    }

    private static string GetRootKey(string key)
    {
        var dotIndex = key.IndexOf('.');
        return dotIndex > 0 ? key.Substring(0, dotIndex) : key;
    }

    private static string GetRemainingKey(string key, string rootKey)
    {
        if (key == rootKey)
            return string.Empty;
        
        if (key.StartsWith(rootKey + "."))
            return key.Substring(rootKey.Length + 1);
        
        return key;
    }

    private static string EscapeYamlValue(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "\"\"";

        // Quote strings that contain special YAML characters or start with special chars
        if (input.Contains(':') || input.Contains('#') || input.Contains('{') || 
            input.Contains('[') || input.Contains('|') || input.Contains('>') ||
            input.Contains('\n') || input.Contains('\'') || input.Contains('"') ||
            input.StartsWith(' ') || input.EndsWith(' '))
        {
            // Use double quotes and escape internal quotes
            return $"\"{input.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }

        return input;
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context));
    }
}
