using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads YAML translation files (one file per language, keys use indentation for hierarchy).</summary>
public class YamlLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Yaml;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.yml", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            var lines = File.ReadAllLines(file);
            items.AddRange(ParseYaml(lang, lines));
        }
        return items;
    }

    private static List<TranslationItem> ParseYaml(string language, string[] lines)
    {
        var result = new List<TranslationItem>(lines.Length / 2);
        var pathStack = new List<(int Indent, string Key)>();

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            ReadOnlySpan<char> span = rawLine.AsSpan();
            var trimmed = span.TrimStart();
            if (trimmed.IsEmpty || trimmed[0] == '#' || trimmed.StartsWith("---")) continue;

            int indent = span.Length - trimmed.Length;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = trimmed[..colonIdx].Trim().ToString();
            var valueSpan = trimmed[(colonIdx + 1)..].Trim();

            // Pop stack to current indent level
            while (pathStack.Count > 0 && pathStack[^1].Indent >= indent)
                pathStack.RemoveAt(pathStack.Count - 1);

            if (valueSpan.IsEmpty)
            {
                pathStack.Add((indent, key));
            }
            else
            {
                var value = StripQuotes(valueSpan.ToString());
                var ns = string.Join('.', pathStack.Select(p => p.Key).Append(key));
                result.Add(new TranslationItem { Language = language, Namespace = ns, Value = value });
            }
        }
        return result;
    }

    private static string StripQuotes(string s) =>
        s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))
            ? s[1..^1].Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"")
            : s;
}
