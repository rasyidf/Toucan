using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads TOML translation files (one file per language, [section] = namespace prefix).</summary>
public class TomlLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Toml;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.toml", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            items.AddRange(ParseToml(lang, File.ReadAllLines(file)));
        }
        return items;
    }

    private static List<TranslationItem> ParseToml(string language, string[] lines)
    {
        var result = new List<TranslationItem>();
        string section = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().Replace("\"", "");
                continue;
            }

            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = line[..eqIdx].Trim().Replace("\"", "");
            var value = StripQuotes(line[(eqIdx + 1)..].Trim());
            var ns = string.IsNullOrEmpty(section) ? key : $"{section}.{key}";

            result.Add(new TranslationItem { Language = language, Namespace = ns, Value = value });
        }
        return result;
    }

    private static string StripQuotes(string s) =>
        s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))
            ? s[1..^1].Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"")
            : s;
}
