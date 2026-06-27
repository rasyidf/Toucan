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

        if (content is string text) { File.WriteAllText(path, text, Encoding.UTF8); return; }
        if (content is byte[] bytes) { File.WriteAllBytes(path, bytes); return; }

        File.WriteAllText(path, JsonSerializer.Serialize(content, s_options), Encoding.UTF8);
    }

    public string ReadText(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
    }

    public void SaveText(string folderPath, string fileName, string content)
    {
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, fileName), content, Encoding.UTF8);
    }

    public byte[] ReadBytes(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : [];
    }

    public void SaveBytes(string folderPath, string fileName, byte[] content)
    {
        Directory.CreateDirectory(folderPath);
        File.WriteAllBytes(Path.Combine(folderPath, fileName), content);
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

        if (content is string text) { await File.WriteAllTextAsync(path, text, Encoding.UTF8).ConfigureAwait(false); return; }
        if (content is byte[] bytes) { await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false); return; }

        var stream = File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, content, s_options).ConfigureAwait(false);
        }
    }

    public Task<string> ReadTextAsync(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllTextAsync(path, Encoding.UTF8) : Task.FromResult(string.Empty);
    }

    public Task SaveTextAsync(string folderPath, string fileName, string content)
    {
        Directory.CreateDirectory(folderPath);
        return File.WriteAllTextAsync(Path.Combine(folderPath, fileName), content, Encoding.UTF8);
    }

    public Task<byte[]> ReadBytesAsync(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        return File.Exists(path) ? File.ReadAllBytesAsync(path) : Task.FromResult<byte[]>([]);
    }

    public Task SaveBytesAsync(string folderPath, string fileName, byte[] content)
    {
        Directory.CreateDirectory(folderPath);
        return File.WriteAllBytesAsync(Path.Combine(folderPath, fileName), content);
    }
}
