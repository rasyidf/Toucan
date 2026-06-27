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
        string? lastField = null; // tracks which field continuation lines belong to

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("msgctxt "))
            {
                currentCtxt = ExtractQuoted(line[8..]);
                lastField = "ctxt";
            }
            else if (line.StartsWith("msgid "))
            {
                currentId = ExtractQuoted(line[6..]);
                lastField = "id";
            }
            else if (line.StartsWith("msgstr "))
            {
                currentStr = ExtractQuoted(line[7..]);
                lastField = "str";
            }
            else if (line.StartsWith('"') && line.EndsWith('"'))
            {
                // Continuation line — append to the last field
                var cont = ExtractQuoted(line);
                if (lastField == "ctxt") currentCtxt += cont;
                else if (lastField == "id") currentId += cont;
                else if (lastField == "str") currentStr += cont;
            }
            else if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                // Blank or comment = entry boundary — emit if we have a pending msgstr
                if (currentStr != null)
                {
                    var key = currentCtxt ?? currentId ?? "";
                    if (!string.IsNullOrEmpty(key))
                        result.Add(new TranslationItem { Language = language, Namespace = key, Value = currentStr });
                }
                currentCtxt = null;
                currentId = null;
                currentStr = null;
                lastField = null;
            }
        }

        // Emit final entry (file may not end with blank line)
        if (currentStr != null)
        {
            var key = currentCtxt ?? currentId ?? "";
            if (!string.IsNullOrEmpty(key))
                result.Add(new TranslationItem { Language = language, Namespace = key, Value = currentStr });
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
