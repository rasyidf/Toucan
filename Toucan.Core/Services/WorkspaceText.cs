using System.Text;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Decodes source documents for document saving.</summary>
public static class WorkspaceText
{
    /// <summary>
    /// A byte-order mark picks the encoding; otherwise strict UTF-8, and bytes that are not UTF-8 are read as ISO-8859-1 (Java
    /// <c>.properties</c> files often are) instead of being replaced, so a save writes them back unchanged.
    /// </summary>
    public static (string Content, SourceTextFormat Format) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var bomReader = new StreamReader(stream, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        try
        {
            var content = bomReader.ReadToEnd();
            var encoding = bomReader.CurrentEncoding;
            var preamble = encoding.GetPreamble();
            return (content, new(encoding.WebName, preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)));
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), new(Encoding.Latin1.WebName, false));
        }
    }
}
