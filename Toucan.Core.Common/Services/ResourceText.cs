using System.Text;
using System.Text.Json;
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
        IndentSize = options.Indent == ResourceIndent.Space4 ? 4 : options.Indent == ResourceIndent.Tab ? 1 : 2
    };

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
