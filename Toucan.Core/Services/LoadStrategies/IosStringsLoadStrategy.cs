using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads iOS/macOS .strings files ("key" = "value";).</summary>
public partial class IosStringsLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.IosStrings;

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

    private static string Unescape(string s) =>
        s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

    [GeneratedRegex("""\"([^"\\]*(?:\\.[^"\\]*)*)"\s*=\s*"([^"\\]*(?:\\.[^"\\]*)*)"\s*;""")]
    private static partial Regex StringsPattern();
}
