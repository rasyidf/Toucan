using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads TOML translation files (one file per language, [section] = namespace prefix).</summary>
public class TomlLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.Toml;

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

    /// <summary>Reads a TOML string value: basic ("...", with escapes) or literal ('...', none). Text after the closing quote (a comment) is ignored.</summary>
    private static string StripQuotes(string s)
    {
        if (s.Length < 2) return s;
        if (s[0] == '\'')
        {
            var end = s.IndexOf('\'', 1);
            return end > 0 ? s[1..end] : s;
        }
        if (s[0] != '"') return s;

        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 1; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') return sb.ToString();
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }

            var n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'u' or 'U':
                    var len = n == 'u' ? 4 : 8;
                    if (i + len < s.Length + 0 && int.TryParse(s.AsSpan(i + 1, len), System.Globalization.NumberStyles.HexNumber, null, out var cp) && cp is >= 0 and <= 0x10FFFF)
                    {
                        sb.Append(char.ConvertFromUtf32(cp));
                        i += len;
                    }
                    else sb.Append('\\').Append(n);
                    break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }
}
