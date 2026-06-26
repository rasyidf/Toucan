using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class JavaPropertiesLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.JavaProperties;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.properties", SearchOption.AllDirectories);
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            // Handle messages_en.properties pattern
            var underscoreIdx = lang.LastIndexOf('_');
            if (underscoreIdx > 0)
                lang = lang[(underscoreIdx + 1)..];

            var lines = File.ReadAllLines(file, Encoding.Latin1);
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimStart();
                if (line.Length == 0 || line[0] == '#' || line[0] == '!') continue;

                var sepIdx = line.IndexOfAny(['=', ':']);
                if (sepIdx <= 0) continue;

                var key = Unescape(line[..sepIdx].TrimEnd());
                var value = Unescape(line[(sepIdx + 1)..].TrimStart());

                items.Add(new TranslationItem { Language = lang, Namespace = key, Value = value });
            }
        }
        return items;
    }

    private static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u' when i + 4 < s.Length:
                        if (ushort.TryParse(s.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var ch))
                        { sb.Append((char)ch); i += 4; }
                        else sb.Append(s[i]);
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
