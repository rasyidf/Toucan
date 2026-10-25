using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Bounded block-map/sequence dialect. Unsupported YAML is diagnosed, never rewritten as strings.</summary>
internal static class YamlDocumentCodec
{
    private sealed record Line(int Indent, string Text, int Number);

    public static JsonNode Read(string text)
    {
        var raw = text.ReplaceLineEndings("\n").Split('\n');
        var lines = new List<Line>();
        var documentSeen = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var value = raw[i].TrimStart(' ');
            var indent = raw[i].Length - value.Length;
            if (value.Length == 0 || value.StartsWith('#')) continue;
            if (value == "---") { if (documentSeen || lines.Count > 0) throw Bad(i, "multiple YAML documents"); documentSeen = true; continue; }
            if (value == "...") { if (raw.Skip(i + 1).Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))) throw Bad(i, "content after document end"); continue; }
            if (value.StartsWith('\t') || value.StartsWith('%') || value.StartsWith('?')) throw Bad(i, "directives, tabs or complex keys");
            var colon = Colon(value);
            var scalar = colon >= 0 ? value[(colon + 1)..].TrimStart() : value.StartsWith("- ", StringComparison.Ordinal) ? value[2..].TrimStart() : value;
            if (scalar.StartsWith('|') || scalar.StartsWith('>'))
            {
                var header = scalar;
                if (header is not ("|" or ">" or "|-" or ">-" or "|+" or ">+")) throw Bad(i, "block scalar modifiers");
                var block = new List<string>();
                var baseIndent = -1;
                var end = i + 1;
                while (end < raw.Length && (string.IsNullOrWhiteSpace(raw[end]) || raw[end].Length - raw[end].TrimStart(' ').Length > indent))
                {
                    if (!string.IsNullOrWhiteSpace(raw[end]) && baseIndent < 0) baseIndent = raw[end].Length - raw[end].TrimStart(' ').Length;
                    if (!string.IsNullOrWhiteSpace(raw[end]) && raw[end].Length - raw[end].TrimStart(' ').Length < baseIndent) throw Bad(end, "block scalar indentation");
                    block.Add(string.IsNullOrWhiteSpace(raw[end]) ? "" : raw[end][Math.Min(baseIndent, raw[end].Length)..]);
                    end++;
                }
                if (end == raw.Length && raw[^1].Length == 0 && block.Count > 0) block.RemoveAt(block.Count - 1);
                var body = header[0] == '|' ? string.Join('\n', block) : Fold(block);
                body = header.EndsWith('-') ? body.TrimEnd('\n') : header.EndsWith('+') ? body + "\n" : body.TrimEnd('\n') + "\n";
                value = value[..(value.Length - scalar.Length)] + JsonSerializer.Serialize(body);
                i = end - 1;
            }
            lines.Add(new(indent, value, i));
        }
        if (lines.Count == 0) return new JsonObject();
        if (lines.Count == 1 && lines[0].Text is "{}" or "[]") return JsonNode.Parse(lines[0].Text)!;
        var at = 0;
        var root = Block(ref at, lines[0].Indent);
        if (at != lines.Count) throw Bad(lines[at].Number, "inconsistent indentation");
        return root;

        JsonNode Block(ref int position, int indentation)
        {
            if (indentation > 256) throw Bad(lines[position].Number, "excessive nesting");
            var sequence = Sequence(lines[position].Text);
            JsonNode result = sequence ? new JsonArray() : new JsonObject();
            while (position < lines.Count)
            {
                var line = lines[position];
                if (line.Indent < indentation) break;
                if (line.Indent > indentation || Sequence(line.Text) != sequence) throw Bad(line.Number, "mixed mapping/sequence or indentation");
                var value = sequence ? line.Text[1..].TrimStart() : line.Text;
                string? key = null;
                if (!sequence)
                {
                    var separator = Colon(value);
                    if (separator < 0) throw Bad(line.Number, "mapping separator");
                    key = String(value[..separator].Trim(), line.Number);
                    if (key == "<<") throw Bad(line.Number, "merge key");
                    value = value[(separator + 1)..].TrimStart();
                    if (((JsonObject)result).ContainsKey(key)) throw Bad(line.Number, "duplicate mapping key");
                }
                else if (Colon(value) >= 0)
                {
                    lines.Insert(position + 1, new(indentation + 2, value, line.Number));
                    value = "";
                }
                position++;
                JsonNode? child = value.Length == 0
                    ? position < lines.Count && lines[position].Indent > indentation ? Block(ref position, lines[position].Indent) : null
                    : Scalar(value, line.Number);
                if (sequence) ((JsonArray)result).Add(child); else ((JsonObject)result)[key!] = child;
            }
            return result;
        }
    }

    private static bool Sequence(string value) => value == "-" || value.StartsWith("- ", StringComparison.Ordinal);
    private static int Colon(string value)
    {
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            if (quote == '"' && value[i] == '\\') { i++; continue; }
            if (quote != '\0') { if (value[i] == quote) quote = '\0'; continue; }
            if (value[i] is '\'' or '"') { quote = value[i]; continue; }
            if (value[i] == ':' && (i + 1 == value.Length || char.IsWhiteSpace(value[i + 1]))) return i;
        }
        return -1;
    }

    private static JsonNode? Scalar(string value, int line)
    {
        if (value.StartsWith('"') || value.StartsWith('\'')) return JsonValue.Create(String(value, line));
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0) value = value[..comment].TrimEnd();
        if (value is "{}" or "[]") return JsonNode.Parse(value);
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        if (bool.TryParse(value, out var boolean)) return JsonValue.Create(boolean);
        if (value.Equals(".nan", StringComparison.OrdinalIgnoreCase) || value.TrimStart('+', '-').Equals(".inf", StringComparison.OrdinalIgnoreCase)) throw Bad(line, "non-finite numeric scalar");
        if (value == "~") return null;
        if (value.Length == 0) return null;
        if (value[0] is '&' or '*' or '!' or '[' or '{' || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            throw Bad(line, "anchors, aliases, tags, flow collections or non-decimal numbers");
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            try { return JsonNode.Parse(value); } catch (JsonException) { throw Bad(line, "non-JSON numeric scalar"); }
        }
        return JsonValue.Create(value);
    }

    private static string String(string value, int line)
    {
        if (value.StartsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(value) ?? ""; } catch (JsonException) { throw Bad(line, "quoted scalar"); }
        }
        if (value.StartsWith('\''))
        {
            if (!value.EndsWith('\'')) throw Bad(line, "multiline quoted scalar");
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        if (value.Length == 0 || value.IndexOfAny(['&', '*', '!', '[', ']', '{', '}']) >= 0) throw Bad(line, "complex key");
        return value;
    }

    private static string Fold(List<string> lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i + 1 < lines.Count)
            {
                if (lines[i + 1].Length == 0 || lines[i].StartsWith(' ') || lines[i + 1].StartsWith(' ') || i == 0 && lines[i].Length == 0) sb.Append('\n');
                else if (lines[i].Length > 0) sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static FormatException Bad(int line, string construct) => new($"line {line + 1}: unsupported or invalid {construct}");

    public static string Write(JsonNode root, ResourceOptions options)
    {
        if (options.Indent is ResourceIndent.None or ResourceIndent.Tab) throw new FormatException("[indent]: block YAML supports two or four spaces.");
        if (root is JsonObject emptyObject && emptyObject.Count == 0 || root is JsonArray emptyArray && emptyArray.Count == 0) return root.ToJsonString() + "\n";
        var sb = new StringBuilder();
        var width = options.Indent == ResourceIndent.Space4 ? 4 : 2;
        Emit(root, 0);
        return sb.ToString();

        void Emit(JsonNode node, int depth)
        {
            if (node is JsonObject obj)
                foreach (var (key, value) in options.Order == ResourceOrder.Alphabetical ? obj.OrderBy(p => p.Key, StringComparer.Ordinal) : obj.AsEnumerable())
                {
                    sb.Append(' ', depth).Append(JsonSerializer.Serialize(key)).Append(':'); Value(value, depth);
                }
            else if (node is JsonArray array)
                foreach (var value in array) { sb.Append(' ', depth).Append('-'); Value(value, depth); }
            else throw new FormatException("A YAML resource root must be a mapping or sequence.");
        }
        void Value(JsonNode? value, int depth)
        {
            if (value is JsonObject o && o.Count > 0 || value is JsonArray a && a.Count > 0) { sb.AppendLine(); Emit(value!, depth + width); }
            else sb.Append(' ').Append(value?.ToJsonString() ?? "null").AppendLine();
        }
    }
}
