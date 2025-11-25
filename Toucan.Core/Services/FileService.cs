using System.IO;
using System.Text;

using Newtonsoft.Json;

using Toucan.Core.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace Toucan.Core.Services;

public class FileService : IFileService
{
    private readonly Microsoft.Extensions.Logging.ILogger<FileService> _logger;
    public FileService(Microsoft.Extensions.Logging.ILogger<FileService> logger)
    {
        _logger = logger;
    }

    public T Read<T>(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
            if (File.Exists(path))
        {
            if (typeof(T) == typeof(string))
            {
                // Read as raw text
                var text = File.ReadAllText(path, Encoding.UTF8);
                return (T)(object)text;
            }

            if (typeof(T) == typeof(byte[]))
            {
                var bytes = File.ReadAllBytes(path);
                return (T)(object)bytes;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json);
        }

            _logger?.LogWarning("File not found: {path}", path);
            return default;
    }

    public void Save<T>(string folderPath, string fileName, T content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var path = Path.Combine(folderPath, fileName);

        if (content is string textContent)
        {
            File.WriteAllText(path, textContent, Encoding.UTF8);
            return;
        }

        if (content is byte[] bytesContent)
        {
            File.WriteAllBytes(path, bytesContent);
            return;
        }

        var fileContent = JsonConvert.SerializeObject(content);
        File.WriteAllText(path, fileContent, Encoding.UTF8);
    }

    public string ReadText(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }

        return null;
    }

    public void SaveText(string folderPath, string fileName, string content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        File.WriteAllText(Path.Combine(folderPath, fileName), content, Encoding.UTF8);
    }

    public byte[] ReadBytes(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        return null;
    }

    public void SaveBytes(string folderPath, string fileName, byte[] content)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        File.WriteAllBytes(Path.Combine(folderPath, fileName), content);
    }

    public void Delete(string folderPath, string fileName)
    {
        if (fileName != null && File.Exists(Path.Combine(folderPath, fileName)))
        {
            File.Delete(Path.Combine(folderPath, fileName));
        }
    }

    // Async wrappers to existing synchronous methods (to avoid breaking changes while adding async support)
    public async Task<T> ReadAsync<T>(string folderPath, string fileName)
    {
        return await Task.Run(() => Read<T>(folderPath, fileName));
    }

    public async Task SaveAsync<T>(string folderPath, string fileName, T content)
    {
        await Task.Run(() => Save(folderPath, fileName, content));
    }

    public async Task<string> ReadTextAsync(string folderPath, string fileName)
    {
        return await Task.Run(() => ReadText(folderPath, fileName));
    }

    public async Task SaveTextAsync(string folderPath, string fileName, string content)
    {
        await Task.Run(() => SaveText(folderPath, fileName, content));
    }

    public async Task<byte[]> ReadBytesAsync(string folderPath, string fileName)
    {
        return await Task.Run(() => ReadBytes(folderPath, fileName));
    }

    public async Task SaveBytesAsync(string folderPath, string fileName, byte[] content)
    {
        await Task.Run(() => SaveBytes(folderPath, fileName, content));
    }
}
