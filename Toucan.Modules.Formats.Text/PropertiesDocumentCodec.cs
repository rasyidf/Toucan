using System.Globalization;
using System.Text;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

internal static class PropertiesDocumentCodec
{
    internal sealed record Line(string Raw, string? Key, string? Value);

    internal static List<Line> Parse(string text)
    {
        var physical = text.ReplaceLineEndings("\n").Split('\n');
        var lines = new List<Line>();
        for (var i = 0; i < physical.Length; i++)
        {
            if (i == physical.Length - 1 && physical[i].Length == 0) break;
            var raw = new StringBuilder();
            var logical = new StringBuilder();
            var first = physical[i].TrimStart(' ', '\t', '\f');
            var comment = first.Length == 0 || first[0] is '#' or '!';
            var continued = false;
            do
            {
                var part = physical[i];
                raw.Append(part);
                if (i < physical.Length - 1) raw.Append('\n');
                if (continued) part = part.TrimStart(' ', '\t', '\f');
                var trailing = part.Reverse().TakeWhile(c => c == '\\').Count();
                continued = !comment && trailing % 2 != 0;
                logical.Append(continued ? part[..^1] : part);
            } while (continued && ++i < physical.Length);
            var content = logical.ToString().TrimStart(' ', '\t', '\f');
            if (content.Length == 0 || content[0] is '#' or '!') { lines.Add(new(raw.ToString(), null, null)); continue; }
            var keyEnd = 0;
            for (; keyEnd < content.Length; keyEnd++)
            {
                if (content[keyEnd] == '\\') { keyEnd++; continue; }
                if (content[keyEnd] is '=' or ':' or ' ' or '\t' or '\f') break;
            }
            keyEnd = Math.Min(keyEnd, content.Length);
            var valueStart = keyEnd;
            while (valueStart < content.Length && content[valueStart] is ' ' or '\t' or '\f') valueStart++;
            if (valueStart < content.Length && content[valueStart] is '=' or ':') valueStart++;
            while (valueStart < content.Length && content[valueStart] is ' ' or '\t' or '\f') valueStart++;
            lines.Add(new(raw.ToString(), Unescape(content[..keyEnd]), Unescape(content[valueStart..])));
        }
        return lines;
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { result.Append(value[i]); continue; }
            if (++i == value.Length) break;
            if (value[i] == 'u')
            {
                if (i + 4 >= value.Length || !ushort.TryParse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                    throw new FormatException("[escape]: Java Unicode escapes require four hexadecimal digits.");
                result.Append((char)code); i += 4;
            }
            else result.Append(value[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'f' => '\f', _ => value[i] });
        }
        return result.ToString();
    }

    internal static string Write(string original, List<TranslationItem> entries, ResourceOptions options)
    {
        var lines = Parse(original);
        var desired = entries.Where(i => options.SaveEmptyTranslations != false || !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, StringComparer.Ordinal);
        var sb = new StringBuilder();
        var escape = options.UnicodeEscapes == ResourceUnicodeEscapes.Always ||
            options.UnicodeEscapes is null or ResourceUnicodeEscapes.Preserve && original.Contains("\\u", StringComparison.Ordinal);
        var latin = options.OutputEncoding is "ISO-8859-1" or "Latin1";
        void Append(TranslationItem item)
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
            sb.Append(Escape(item.Namespace, true, escape, latin)).Append('=').Append(Escape(item.Value ?? "", false, escape, latin)).Append('\n');
        }
        if (options.Order == ResourceOrder.Alphabetical)
        {
            foreach (var line in lines.Where(l => l.Key == null)) sb.Append(line.Raw);
            foreach (var item in desired.Values.OrderBy(i => i.Namespace, StringComparer.Ordinal)) Append(item);
            return sb.ToString();
        }
        foreach (var line in lines)
        {
            if (line.Key is null) { sb.Append(line.Raw); continue; }
            if (!desired.Remove(line.Key, out var item)) continue;
            if (item.Value == line.Value && options.UnicodeEscapes is null or ResourceUnicodeEscapes.Preserve) sb.Append(line.Raw);
            else Append(item);
        }
        foreach (var item in desired.Values) Append(item);
        return sb.ToString();
    }

    private static string Escape(string value, bool key, bool unicode, bool latin)
    {
        var result = new StringBuilder();
        var leading = true;
        foreach (var ch in value)
        {
            if (ch == ' ' && (key || leading)) { result.Append("\\ "); continue; }
            leading = false;
            if ((unicode && ch > 127) || (latin && ch > 255)) { result.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture)); continue; }
            result.Append(ch switch { '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\f' => "\\f",
                '=' when key => "\\=", ':' when key => "\\:", '#' when key => "\\#", '!' when key => "\\!", _ => ch.ToString() });
        }
        return result.ToString();
    }
}
