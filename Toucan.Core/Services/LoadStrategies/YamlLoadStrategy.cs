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
        var result = new List<TranslationItem>();
        var pathStack = new List<(int Indent, string Key)>();

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith('#') || rawLine.TrimStart() == "---")
                continue;

            int indent = rawLine.Length - rawLine.TrimStart().Length;
            var content = rawLine.TrimStart();

            var colonIdx = content.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = content[..colonIdx].Trim();
            var value = content[(colonIdx + 1)..].Trim();

            // Pop stack to current indent level
            while (pathStack.Count > 0 && pathStack[^1].Indent >= indent)
                pathStack.RemoveAt(pathStack.Count - 1);

            if (string.IsNullOrEmpty(value))
            {
                // Parent node
                pathStack.Add((indent, key));
            }
            else
            {
                // Leaf node — strip quotes
                value = StripQuotes(value);
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
