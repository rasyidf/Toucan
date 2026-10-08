using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class YamlSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Yaml;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "YAML 1.1 and 1.2 scalar maps; one file per language",
        ["Nested and flat dotted keys (same style as the file)", "block scalars (| and >) read", "multiline text", "quoted scalars such as on", "off", "~ and numbers"],
        ["Comments in the file are not kept", "Anchors, aliases, tags and sequences are not read", "Block scalars are written as quoted strings"]);
    public string DisplayName => "YAML";
    public IReadOnlyList<string> FileExtensions => [".yml", ".yaml"];
    public string DefaultFilePath(string language) => $"{language}.yaml";

    public FormatDetection Detection { get; } = new(8, [".yaml", ".yml"], []);

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

            // Keep the style the file already uses; a key that is also a parent can only be written flat.
            if (UseFlatKeys(path, language, dict))
                WriteFlat(sb, dict);
            else
                WriteYamlDict(sb, dict, 0);

            fileService.SaveText(path, language + ".yaml", sb.ToString());
        }
    }

    private static bool UseFlatKeys(string path, string language, Dictionary<string, string> dict)
    {
        if (HasKeyClash(dict)) return true;
        foreach (var ext in new[] { ".yaml", ".yml" })
        {
            var existing = Path.Combine(path, language + ext);
            if (File.Exists(existing)) return ExistingFileIsFlat(existing);
        }
        return false;
    }

    /// <summary>True when a key is also the parent of another key (<c>app</c> and <c>app.title</c>).</summary>
    private static bool HasKeyClash(Dictionary<string, string> dict) =>
        dict.Keys.Any(k => dict.Keys.Any(o => o.Length > k.Length && o.StartsWith(k + ".", StringComparison.Ordinal)));

    private static bool ExistingFileIsFlat(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] == ' ' || line[0] == '\t' || line[0] == '#' || line[0] == '-') continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().Trim('"', '\'');
            if (key.Contains('.')) return true;
        }
        return false;
    }

    private static void WriteFlat(StringBuilder sb, Dictionary<string, string> dict)
    {
        foreach (var kv in dict.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine($"{EscapeYamlKey(kv.Key)}: {EscapeYamlValue(kv.Value)}");
    }

    private static string EscapeYamlKey(string key) =>
        key.IndexOfAny([':', '#', '"', '\'', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '%', '@', '`']) >= 0
        || key.StartsWith(' ') || key.EndsWith(' ') || key.StartsWith('-') || key.StartsWith('?')
            ? $"\"{key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : key;

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
                // Nested structure; clashes between a value and a parent never reach here (see HasKeyClash)
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

        // Values that YAML would interpret as special types need quoting
        bool needsQuote = input.Contains(':') || input.Contains('#') || input.Contains('{') || 
            input.Contains('[') || input.Contains('|') || input.Contains('>') ||
            input.Contains('\n') || input.Contains('\r') || input.Contains('\t') ||
            input.Contains('\'') || input.Contains('"') ||
            input.StartsWith(' ') || input.EndsWith(' ') ||
            input.StartsWith('*') || input.StartsWith('&') || input.StartsWith('!') ||
            input.StartsWith('@') || input.StartsWith('`') ||
            string.Equals(input, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "n", StringComparison.OrdinalIgnoreCase) ||
            input == "~" || input == "-" || input.StartsWith("- ") || input.StartsWith('?') ||
            input.StartsWith('%') || input.StartsWith(',') ||
            double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ||
            (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || input.StartsWith("0o", StringComparison.OrdinalIgnoreCase));

        if (needsQuote)
        {
            // Escape backslashes, quotes, and control characters
            var escaped = input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return $"\"{escaped}\"";
        }

        return input;
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context)).ConfigureAwait(false);
    }
}
