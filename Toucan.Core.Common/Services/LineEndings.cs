using System.IO;
using System.Text;

namespace Toucan.Core.Services;

/// <summary>
/// The line-ending policy for every text file Toucan writes into a project, so a project moved between Windows, macOS
/// and Linux does not change whole on its next save:
/// <list type="bullet">
/// <item>A file that exists keeps the line endings it has.</item>
/// <item>A new file takes the line endings of the project's files next to it (the same extension in the same folder,
/// then the same file name in sibling folders, as for <c>locales/fr/common.json</c> beside <c>locales/en/</c>).</item>
/// <item>With nothing to copy, the operating system's newline.</item>
/// </list>
/// A line ending set explicitly for the project (Project Properties) overrides all of this and is applied by its writer.
/// </summary>
public static class LineEndings
{
    public const string Lf = "\n";
    public const string CrLf = "\r\n";

    /// <summary>Characters read when detecting a file's line endings; enough for any real file's first lines.</summary>
    private const int SampleLength = 64 * 1024;

    /// <summary>The newline used when a new file has nothing to copy.</summary>
    public static string Default => Environment.NewLine;

    /// <summary>The line ending most lines of <paramref name="text"/> use, or null when it has no line break.</summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int crlf = 0, lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
        }
        return crlf == 0 && lf == 0 ? null : crlf > lf ? CrLf : Lf;
    }

    /// <summary>The line ending of the file at <paramref name="path"/>, or null when it does not exist or has no line break.</summary>
    public static string? DetectFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            var buffer = new char[SampleLength];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return Detect(new string(buffer, 0, read));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The line ending to write <paramref name="path"/> with, by the policy above.</summary>
    public static string For(string path) => DetectFile(path) ?? FromNeighbours(path) ?? Default;

    /// <summary>
    /// <paramref name="text"/> with every LF and CRLF line break written as <paramref name="newline"/>. A lone CR and other
    /// Unicode separators (U+2028, NEL, form feed) are left alone, unlike <see cref="string.ReplaceLineEndings(string)"/>:
    /// they can be characters of a value in formats that write values unescaped.
    /// </summary>
    public static string Apply(string text, string newline)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (newline == Lf) return text.Replace(CrLf, Lf, StringComparison.Ordinal);
        if (!text.Contains('\n', StringComparison.Ordinal)) return text;
        var builder = new StringBuilder(text.Length + text.Length / 16);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r')) builder.Append('\r');
            builder.Append(text[i]);
        }
        return builder.ToString();
    }

    /// <summary><paramref name="text"/> with the line endings <paramref name="path"/> should be written with.</summary>
    public static string ForFile(string path, string text) => Apply(text, For(path));

    /// <summary>
    /// A neighbouring file's line ending: another file with the same extension in the folder, then the same relative path
    /// with one folder swapped, up to three levels up (<c>locales/en/LC_MESSAGES/messages.po</c> for <c>locales/fr/…</c>).
    /// </summary>
    private static string? FromNeighbours(string path)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (folder is null) return null;
        try
        {
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*" + Path.GetExtension(full)).Where(f => !string.Equals(f, full, StringComparison.Ordinal)).Take(8))
                    if (DetectFile(file) is { } found) return found;
            var rest = Path.GetFileName(full);
            var swapped = folder;
            for (var level = 0; level < 3 && Path.GetDirectoryName(swapped) is { } parent; level++)
            {
                if (Directory.Exists(parent))
                    foreach (var sibling in Directory.EnumerateDirectories(parent).Where(d => !string.Equals(d, swapped, StringComparison.Ordinal)).Take(32))
                        if (DetectFile(Path.Combine(sibling, rest)) is { } found) return found;
                rest = Path.Combine(Path.GetFileName(swapped), rest);
                swapped = parent;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
