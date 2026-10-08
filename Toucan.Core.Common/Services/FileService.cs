using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Services;

public class FileService(ILogger<FileService> logger) : IFileService
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public T Read<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (!File.Exists(path))
        {
            logger.LogWarning("File not found: {Path}", path);
            return default!;
        }

        if (typeof(T) == typeof(string)) return (T)(object)File.ReadAllText(path, Encoding.UTF8);
        if (typeof(T) == typeof(byte[])) return (T)(object)File.ReadAllBytes(path);

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, s_options)!;
    }

    public void Save<T>(string folderPath, string fileName, T content)
    {
        Directory.CreateDirectory(folderPath);
        var path = Path.Combine(folderPath, fileName);

        if (content is string text) { WriteAtomic(path, Utf8Bom.GetBytes(text)); return; }
        if (content is byte[] bytes) { WriteAtomic(path, bytes); return; }

        WriteAtomic(path, Utf8Bom.GetBytes(JsonSerializer.Serialize(content, s_options)));
    }

    public string ReadText(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
    }

    public void SaveText(string folderPath, string fileName, string content)
    {
        Directory.CreateDirectory(folderPath);
        WriteAtomic(Path.Combine(folderPath, fileName), Utf8Bom.GetBytes(content));
    }

    public byte[] ReadBytes(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : [];
    }

    public void SaveBytes(string folderPath, string fileName, byte[] content)
    {
        Directory.CreateDirectory(folderPath);
        WriteAtomic(Path.Combine(folderPath, fileName), content);
    }

    public void Delete(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public async Task<T> ReadAsync<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (!File.Exists(path)) return default!;

        if (typeof(T) == typeof(string)) return (T)(object)await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
        if (typeof(T) == typeof(byte[])) return (T)(object)await File.ReadAllBytesAsync(path).ConfigureAwait(false);

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return (await JsonSerializer.DeserializeAsync<T>(stream, s_options).ConfigureAwait(false))!;
        }
    }

    public async Task SaveAsync<T>(string folderPath, string fileName, T content)
    {
        Directory.CreateDirectory(folderPath);
        var path = Path.Combine(folderPath, fileName);

        if (content is string text) { await WriteAtomicAsync(path, Utf8Bom.GetBytes(text)).ConfigureAwait(false); return; }
        if (content is byte[] bytes) { await WriteAtomicAsync(path, bytes).ConfigureAwait(false); return; }

        await WriteAtomicAsync(path, Utf8Bom.GetBytes(JsonSerializer.Serialize(content, s_options))).ConfigureAwait(false);
    }

    public Task<string> ReadTextAsync(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllTextAsync(path, Encoding.UTF8) : Task.FromResult(string.Empty);
    }

    public Task SaveTextAsync(string folderPath, string fileName, string content)
    {
        Directory.CreateDirectory(folderPath);
        return WriteAtomicAsync(Path.Combine(folderPath, fileName), Utf8Bom.GetBytes(content));
    }

    public Task<byte[]> ReadBytesAsync(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllBytesAsync(path) : Task.FromResult<byte[]>([]);
    }

    public Task SaveBytesAsync(string folderPath, string fileName, byte[] content)
    {
        Directory.CreateDirectory(folderPath);
        return WriteAtomicAsync(Path.Combine(folderPath, fileName), content);
    }

    // File.WriteAllText(path, text, Encoding.UTF8) writes a BOM; keep that behaviour byte-for-byte.
    private static readonly Utf8BomEncoding Utf8Bom = new();

    private sealed class Utf8BomEncoding
    {
        public byte[] GetBytes(string text)
        {
            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(text);
            var all = new byte[preamble.Length + body.Length];
            preamble.CopyTo(all, 0);
            body.CopyTo(all, preamble.Length);
            return all;
        }
    }

    /// <summary>
    /// Stages <paramref name="bytes"/> in a temporary file next to <paramref name="path"/>, flushes it to disk, and
    /// swaps it in. If staging or the swap fails the existing file is left untouched and the temp file is removed.
    /// </summary>
    internal static void WriteAtomic(string path, byte[] bytes)
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

    internal static async Task WriteAtomicAsync(string path, byte[] bytes)
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

    private static string StageTempPath(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static void Commit(string temp, string path, int expectedLength)
    {
        // Validate the staged copy before it replaces anything.
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
