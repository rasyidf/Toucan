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

            var rawLines = File.ReadAllLines(file, Encoding.Latin1);
            var lines = JoinContinuationLines(rawLines);

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!') continue;

                var sepIdx = trimmed.IndexOfAny(['=', ':']);
                if (sepIdx <= 0) continue;

                var key = Unescape(trimmed[..sepIdx].TrimEnd());
                var value = Unescape(trimmed[(sepIdx + 1)..].TrimStart());

                items.Add(new TranslationItem { Language = lang, Namespace = key, Value = value });
            }
        }
        return items;
    }

    /// <summary>
    /// Joins logical lines: a trailing unescaped backslash means the next line is a continuation.
    /// Leading whitespace on continuation lines is stripped per the .properties spec.
    /// </summary>
    private static List<string> JoinContinuationLines(string[] rawLines)
    {
        var result = new List<string>(rawLines.Length);
        var sb = new StringBuilder();
        bool continuing = false;

        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];

            // Strip leading whitespace on continuation lines
            if (continuing)
                line = line.TrimStart();

            if (EndsWithContinuation(line))
            {
                // Strip the trailing backslash and append
                sb.Append(line, 0, line.Length - 1);
                continuing = true;
            }
            else
            {
                sb.Append(line);
                result.Add(sb.ToString());
                sb.Clear();
                continuing = false;
            }
        }

        // Flush any trailing continued line (file ended mid-continuation)
        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }

    /// <summary>
    /// Returns true if the line ends with an odd number of backslashes (unescaped continuation).
    /// </summary>
    private static bool EndsWithContinuation(string line)
    {
        int trailingBackslashes = 0;
        for (int i = line.Length - 1; i >= 0 && line[i] == '\\'; i--)
            trailingBackslashes++;
        return trailingBackslashes % 2 == 1;
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
