using System.Text;

namespace Toucan.Core.Services;

/// <summary>Applies explicit project output preferences to translation files.</summary>
public static class ProjectTextFormat
{
    public static void Apply(string path, string? encoding, string? lineEnding, Encoding? fallback = null)
    {
        if (encoding is not (null or "UTF-8" or "UTF-8 BOM")) throw new ArgumentException("Unsupported encoding.", nameof(encoding));
        if (lineEnding is not (null or "LF" or "CRLF")) throw new ArgumentException("Unsupported line ending.", nameof(lineEnding));
        if (encoding == null && lineEnding == null || !File.Exists(path)) return;
        string text;
        Encoding detected;
        using (var reader = new StreamReader(path, fallback ?? new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
            detected = reader.CurrentEncoding;
        }
        var output = encoding == null ? detected : new UTF8Encoding(encoding == "UTF-8 BOM");
        if (lineEnding != null) text = text.ReplaceLineEndings(lineEnding == "LF" ? "\n" : "\r\n");
        File.WriteAllText(path, text, output);
    }
}
