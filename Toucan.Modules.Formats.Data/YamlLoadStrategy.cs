using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads YAML translation files (one file per language, keys use indentation for hierarchy).</summary>
public class YamlLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.Yaml;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = FileEnumerator.EnumerateFiles(folder, "*.yaml")
            .Concat(FileEnumerator.EnumerateFiles(folder, "*.yml"));

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

        int i = 0;
        while (i < lines.Length)
        {
            var rawLine = lines[i];
            if (string.IsNullOrWhiteSpace(rawLine)) { i++; continue; }

            ReadOnlySpan<char> span = rawLine.AsSpan();
            var trimmed = span.TrimStart();
            if (trimmed.IsEmpty || trimmed[0] == '#' || trimmed.StartsWith("---")) { i++; continue; }

            int indent = span.Length - trimmed.Length;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) { i++; continue; }

            var key = StripQuotes(trimmed[..colonIdx].Trim().ToString());
            var valueSpan = trimmed[(colonIdx + 1)..].Trim();

            // Pop stack to current indent level
            while (pathStack.Count > 0 && pathStack[^1].Indent >= indent)
                pathStack.RemoveAt(pathStack.Count - 1);

            if (valueSpan.IsEmpty)
            {
                pathStack.Add((indent, key));
                i++;
            }
            else if (valueSpan.Length == 1 && (valueSpan[0] == '|' || valueSpan[0] == '>'))
            {
                // Multi-line block scalar: collect subsequent indented lines
                bool literal = valueSpan[0] == '|';
                i++;
                var blockIndent = -1;
                var blockLines = new List<string>();
                while (i < lines.Length)
                {
                    var bLine = lines[i];
                    if (string.IsNullOrWhiteSpace(bLine)) { blockLines.Add(""); i++; continue; }
                    var bTrimLen = bLine.Length - bLine.AsSpan().TrimStart().Length;
                    if (bTrimLen <= indent) break; // back to same or lower indent — block ended
                    if (blockIndent < 0) blockIndent = bTrimLen;
                    blockLines.Add(bTrimLen >= blockIndent ? bLine[blockIndent..] : bLine.TrimStart());
                    i++;
                }
                // Trim trailing empty lines
                while (blockLines.Count > 0 && string.IsNullOrEmpty(blockLines[^1]))
                    blockLines.RemoveAt(blockLines.Count - 1);
                var value = literal ? string.Join("\n", blockLines) : string.Join(" ", blockLines.Select(l => l.Length == 0 ? "\n" : l));
                var ns = string.Join('.', pathStack.Select(p => p.Key).Append(key));
                result.Add(new TranslationItem { Language = language, Namespace = ns, Value = value });
            }
            else
            {
                var value = StripQuotes(valueSpan.ToString());
                // Handle __self convention for keys that are both parents and values
                var resolvedKey = key == "__self"
                    ? string.Join('.', pathStack.Select(p => p.Key))
                    : string.Join('.', pathStack.Select(p => p.Key).Append(key));
                if (!string.IsNullOrEmpty(resolvedKey))
                    result.Add(new TranslationItem { Language = language, Namespace = resolvedKey, Value = value });
                i++;
            }
        }
        return result;
    }

    private static string StripQuotes(string s)
    {
        if (s.Length < 2) return s;
        if (s[0] == '\'' && s[^1] == '\'') return s[1..^1].Replace("''", "'");
        if (s[0] != '"' || s[^1] != '"') return s;

        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 1; i < s.Length - 1; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length - 1) { sb.Append(s[i]); continue; }
            i++;
            sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => s[i] });
            if (s[i] is not ('n' or 't' or 'r' or '"' or '\\')) sb.Insert(sb.Length - 1, '\\');
        }
        return sb.ToString();
    }
}
