using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads PO/POT (gettext) translation files.</summary>
public partial class PoLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Properties;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.po", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.pot", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            items.AddRange(ParsePo(lang, File.ReadAllText(file)));
        }
        return items;
    }

    private static List<TranslationItem> ParsePo(string language, string content)
    {
        var result = new List<TranslationItem>();
        string? currentCtxt = null;
        string? currentId = null;
        string? currentStr = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("msgctxt "))
                currentCtxt = ExtractQuoted(line[8..]);
            else if (line.StartsWith("msgid "))
                currentId = ExtractQuoted(line[6..]);
            else if (line.StartsWith("msgstr "))
            {
                currentStr = ExtractQuoted(line[7..]);
                // Emit entry
                var key = currentCtxt ?? currentId ?? "";
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(currentStr))
                    result.Add(new TranslationItem { Language = language, Namespace = key, Value = currentStr });
                currentCtxt = null;
                currentId = null;
                currentStr = null;
            }
        }
        return result;
    }

    private static string ExtractQuoted(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1].Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"");
        return s;
    }
}
