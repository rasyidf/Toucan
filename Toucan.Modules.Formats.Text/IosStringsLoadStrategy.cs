using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads iOS/macOS .strings files ("key" = "value";).</summary>
public partial class IosStringsLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.IosStrings;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.strings", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            // Language from parent .lproj dir or filename
            var lang = DetectLanguage(file);
            items.AddRange(ParseStrings(lang, File.ReadAllText(file)));
        }
        return items;
    }

    private static string DetectLanguage(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? "";
        var dirName = Path.GetFileName(dir);
        if (dirName.EndsWith(".lproj"))
            return dirName[..^6]; // "en.lproj" → "en"
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static List<TranslationItem> ParseStrings(string language, string content)
    {
        var result = new List<TranslationItem>();
        var matches = StringsPattern().Matches(content);
        foreach (Match m in matches)
        {
            var key = Unescape(m.Groups[1].Value);
            var value = Unescape(m.Groups[2].Value);
            result.Add(new TranslationItem { Language = language, Namespace = key, Value = value });
        }
        return result;
    }

    private static string Unescape(string s)
    {
        // ponytail: single-pass to avoid \\n being misinterpreted as \+newline
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case '\\': sb.Append('\\'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    default: sb.Append(s[i]); break; // unknown escape: keep backslash
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex("""\"([^"\\]*(?:\\.[^"\\]*)*)"\s*=\s*"([^"\\]*(?:\\.[^"\\]*)*)"\s*;""")]
    private static partial Regex StringsPattern();
}
