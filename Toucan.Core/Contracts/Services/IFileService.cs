namespace Toucan.Core.Contracts.Services;

public interface IFileService
{
    // Generic JSON-backed read (deserializes JSON to T). For raw read of text or bytes use ReadText/ReadBytes.
    T Read<T>(string folderPath, string fileName);

    // Generic JSON-backed save (serializes content to JSON). For raw writing of text or bytes use SaveText/SaveBytes.
    void Save<T>(string folderPath, string fileName, T content);

    // Read raw text content of a file. Useful for non-JSON formats like INI/YAML.
    string ReadText(string folderPath, string fileName);

    // Save raw text content as file.
    void SaveText(string folderPath, string fileName, string content);

    // Read raw bytes of a file.
    byte[] ReadBytes(string folderPath, string fileName);

    // Write raw bytes to a file.
    void SaveBytes(string folderPath, string fileName, byte[] content);

    void Delete(string folderPath, string fileName);

    // Async variants (additive, do not remove sync signatures yet)
    Task<T> ReadAsync<T>(string folderPath, string fileName);
    Task SaveAsync<T>(string folderPath, string fileName, T content);
    Task<string> ReadTextAsync(string folderPath, string fileName);
    Task SaveTextAsync(string folderPath, string fileName, string content);
    Task<byte[]> ReadBytesAsync(string folderPath, string fileName);
    Task SaveBytesAsync(string folderPath, string fileName, byte[] content);
}
