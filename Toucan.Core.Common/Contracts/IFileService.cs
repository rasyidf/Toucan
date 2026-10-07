namespace Toucan.Core.Contracts.Services;

public interface IFileService
{
    T Read<T>(string folderPath, string fileName);
    void Save<T>(string folderPath, string fileName, T content);
    string ReadText(string folderPath, string fileName);
    void SaveText(string folderPath, string fileName, string content);
    byte[] ReadBytes(string folderPath, string fileName);
    void SaveBytes(string folderPath, string fileName, byte[] content);
    void Delete(string folderPath, string fileName);

    Task<T> ReadAsync<T>(string folderPath, string fileName);
    Task SaveAsync<T>(string folderPath, string fileName, T content);
    Task<string> ReadTextAsync(string folderPath, string fileName);
    Task SaveTextAsync(string folderPath, string fileName, string content);
    Task<byte[]> ReadBytesAsync(string folderPath, string fileName);
    Task SaveBytesAsync(string folderPath, string fileName, byte[] content);
}
