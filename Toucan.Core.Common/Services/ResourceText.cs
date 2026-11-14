using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>One text boundary for exact resources. Output encoding never reinterprets input bytes.</summary>
public static class ResourceText
{
    public static ResourceOptions Options(ResourceReadRequest request) =>
        Validate(ResourceOptions.Resolve(request.Resource.Options, request.ProjectOptions, null, null), request.Resource.RelativePath);
    public static ResourceOptions Options(ResourceWriteRequest request) =>
        Validate(ResourceOptions.Resolve(request.Document.Resource.Options, request.ProjectOptions, request.Document.DetectedOptions, null), request.Document.Resource.RelativePath);

    public static ResourceOptions Validate(ResourceOptions options, string relative)
    {
        Check(options.Layout, "layout"); Check(options.Arrays, "arrays"); Check(options.Indent, "indent");
        Check(options.Order, "order"); Check(options.Newline, "newline"); Check(options.UnicodeEscapes, "unicodeEscapes");
        if (options.Delimiter is { } delimiter && (delimiter.Length == 0 || delimiter.IndexOfAny(['\\', '[', ']']) >= 0))
            throw new FormatException($"{relative} [delimiter]: a delimiter must be nonempty and cannot contain backslashes or brackets.");
        EncodingFor(options.InputEncoding, relative, "inputEncoding"); EncodingFor(options.OutputEncoding, relative, "outputEncoding");
        return options;
        void Check<T>(T? value, string field) where T : struct, Enum
        {
            if (value is { } selected && !Enum.IsDefined(selected)) throw new FormatException($"{relative} [{field}]: unsupported setting.");
        }
    }

    public static Encoding EncodingFor(string? name, string relative, string field, bool latin = false) => name switch
    {
        null => latin ? Encoding.Latin1 : new UTF8Encoding(false, true),
        "UTF-8" => new UTF8Encoding(false, true),
        "UTF-8 BOM" => new UTF8Encoding(true, true),
        "ISO-8859-1" or "Latin1" => Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new FormatException($"{relative} [{field}]: unsupported encoding '{name}'.")
    };

    public static string Read(ResourceReadRequest request) => Read(request, out _);

    public static string Read(ResourceReadRequest request, out string detectedEncoding)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var path = ResourceFilePath.Resolve(request.ProjectRoot, request.Resource.RelativePath);
        var options = Options(request);
        var bytes = File.ReadAllBytes(path);
        var encoding = EncodingFor(options.InputEncoding, request.Resource.RelativePath, "inputEncoding");
        detectedEncoding = encoding.CodePage == 28591 ? "ISO-8859-1" : bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? "UTF-8 BOM" : "UTF-8";
        var text = encoding.GetString(bytes);
        return text.TrimStart('\uFEFF');
    }

    public static ResourceOptions Detect(string content, string? encoding = null)
    {
        var indentation = content.ReplaceLineEndings("\n").Split('\n').Where(l => l.Length > 0 && l[0] == ' ')
            .Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
        return new ResourceOptions
        {
            InputEncoding = encoding, OutputEncoding = encoding, Order = ResourceOrder.Preserve,
            Newline = content.Contains("\r\n", StringComparison.Ordinal) ? ResourceNewline.CrLf : ResourceNewline.Lf,
            Indent = content.Contains("\n\t", StringComparison.Ordinal) ? ResourceIndent.Tab : indentation >= 4 ? ResourceIndent.Space4 : content.Contains('\n') ? ResourceIndent.Space2 : null
        };
    }

    public static JsonSerializerOptions JsonOptions(ResourceOptions options) => new()
    {
        WriteIndented = options.Indent != ResourceIndent.None,
        IndentCharacter = options.Indent == ResourceIndent.Tab ? '\t' : ' ',
        IndentSize = options.Indent == ResourceIndent.Space4 ? 4 : options.Indent == ResourceIndent.Tab ? 1 : 2,
        Encoder = options.UnicodeEscapes == ResourceUnicodeEscapes.Minimal ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : null
    };

    /// <summary>Serializes with <see cref="JsonOptions"/>. Minimal writes every character raw except what JSON
    /// requires escaped (quote, backslash, control characters) and U+2028/U+2029.</summary>
    public static string ToJsonString(JsonNode node, ResourceOptions options)
    {
        var text = node.ToJsonString(JsonOptions(options));
        return options.UnicodeEscapes == ResourceUnicodeEscapes.Minimal ? UnescapeOptional(text) : text;
    }

    /// <summary>Always when the document escapes a character that Minimal would write raw; otherwise Minimal.</summary>
    public static ResourceUnicodeEscapes DetectJsonEscapes(string json)
    {
        for (var i = 0; i < json.Length - 1; i++)
        {
            if (json[i] != '\\') continue;
            if (json[++i] == 'u' && HexAt(json, i + 1) is { } code && (char.IsSurrogate(code) || !MustEscape(code))) return ResourceUnicodeEscapes.Always;
        }
        return ResourceUnicodeEscapes.Minimal;
    }

    // The relaxed encoder still escapes non-BMP characters, NBSP and other blocklisted code points.
    private static string UnescapeOptional(string json)
    {
        if (!json.Contains("\\u", StringComparison.Ordinal)) return json;
        var result = new StringBuilder(json.Length);
        for (var i = 0; i < json.Length; i++)
        {
            if (json[i] != '\\' || i + 1 == json.Length) { result.Append(json[i]); continue; }
            if (json[i + 1] != 'u' || HexAt(json, i + 2) is not { } code) { result.Append(json, i, 2); i++; continue; }
            if (char.IsHighSurrogate(code) && i + 12 <= json.Length && json.AsSpan(i + 6).StartsWith("\\u") && HexAt(json, i + 8) is { } low && char.IsLowSurrogate(low))
            { result.Append(code).Append(low); i += 11; }
            else if (MustEscape(code)) { result.Append(json, i, 6); i += 5; }
            else { result.Append(code); i += 5; }
        }
        return result.ToString();
    }

    private static bool MustEscape(char code) => code < ' ' || code is '"' or '\\' or '\u2028' or '\u2029' || char.IsSurrogate(code);

    private static char? HexAt(string text, int start) =>
        start + 4 <= text.Length && ushort.TryParse(text.AsSpan(start, 4), System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture, out var code) ? (char)code : null;

    public static void Write(ResourceWriteRequest request, string content, bool latin = false)
    {
        var options = Options(request);
        var path = ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        var encoding = EncodingFor(options.OutputEncoding, request.Document.Resource.RelativePath, "outputEncoding", latin);
        content = content.ReplaceLineEndings(options.Newline == ResourceNewline.CrLf ? "\r\n" : "\n");
        request.CancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, content, encoding);
        var writtenEncoding = encoding.CodePage == 28591 ? "ISO-8859-1" : encoding.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8";
        // A transcoded native document must decode its newly written bytes on reopen.
        if (options.OutputEncoding != null && writtenEncoding != (options.InputEncoding ?? request.Document.DetectedOptions.InputEncoding ?? "UTF-8"))
            request.Document.Resource = request.Document.Resource with { Options = request.Document.Resource.Options with { InputEncoding = writtenEncoding } };
        request.Document.DetectedOptions = Detect(content, writtenEncoding);
    }

    public static IReadOnlyList<ResourcePathSegment> NewPath(string key, ResourceOptions options, bool literal = false)
    {
        if (literal || options.Layout is ResourceLayout.Literal or ResourceLayout.Dotted) return [ResourcePathSegment.Named(key)];
        var delimiter = options.Delimiter ?? ".";
        if (delimiter.Length == 0 || delimiter.Contains('\\') || delimiter.IndexOfAny(['[', ']']) >= 0)
            throw new FormatException("[delimiter]: a delimiter must be nonempty and cannot contain backslashes or brackets.");
        if (delimiter == ".") return ResourceEntryOperations.ParseSelector(key);
        var selector = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            if (key[i] == '\\' && i + 1 < key.Length) { selector.Append(key[i]).Append(key[++i]); continue; }
            if (key.AsSpan(i).StartsWith(delimiter, StringComparison.Ordinal)) { selector.Append('.'); i += delimiter.Length - 1; }
            else { if (key[i] == '.') selector.Append('\\'); selector.Append(key[i]); }
        }
        return ResourceEntryOperations.ParseSelector(selector.ToString());
    }
}
