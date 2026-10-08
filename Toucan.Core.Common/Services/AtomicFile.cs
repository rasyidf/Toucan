using System.IO;
using System.Text;

namespace Toucan.Core.Services;

/// <summary>
/// Writes files so a crash or write error never leaves a half-written file: the content is staged in a temporary
/// file next to the target, flushed to disk, checked, and then swapped in. On failure the existing file is untouched
/// and the temporary file is removed.
/// </summary>
public static class AtomicFile
{
    /// <summary>Same bytes as <c>File.WriteAllText(path, text, encoding)</c>: the encoding's preamble (BOM) is included.</summary>
    public static byte[] GetBytes(string text, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        if (preamble.Length == 0) return body;
        var all = new byte[preamble.Length + body.Length];
        preamble.CopyTo(all, 0);
        body.CopyTo(all, preamble.Length);
        return all;
    }

    public static void WriteAllText(string path, string text, Encoding? encoding = null) =>
        WriteAllBytes(path, GetBytes(text, encoding ?? Encoding.UTF8));

    public static Task WriteAllTextAsync(string path, string text, Encoding? encoding = null) =>
        WriteAllBytesAsync(path, GetBytes(text, encoding ?? Encoding.UTF8));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var temp = StageTempPath(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            Commit(temp, path, bytes.Length);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public static async Task WriteAllBytesAsync(string path, byte[] bytes)
    {
        var temp = StageTempPath(path);
        try
        {
            var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            Commit(temp, path, bytes.Length);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static string StageTempPath(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
    }

    private static void Commit(string temp, string path, int expectedLength)
    {
        if (new FileInfo(temp).Length != expectedLength)
            throw new IOException($"Staged file for '{path}' is incomplete; the original was not changed.");

        if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
