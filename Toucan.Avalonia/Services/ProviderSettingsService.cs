using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Persists translation provider settings app-wide (~/Documents/Toucan/providers.json) or per project
/// (&lt;project&gt;/.toucan/providers.json). Secrets are encrypted through <see cref="ISecureStorageService"/>.
/// </summary>
public sealed class ProviderSettingsService(ISecureStorageService secureStorage) : IProviderSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static string AppFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "providers.json");

    private static string ProjectFilePath(string projectPath) => Path.Combine(projectPath, ".toucan", "providers.json");

    public IEnumerable<ProviderSettings> LoadAppProviderSettings() => LoadFromFile(AppFilePath);

    public void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings) => SaveToFile(AppFilePath, settings);

    public IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath) => LoadFromFile(ProjectFilePath(projectPath));

    public void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings) =>
        SaveToFile(ProjectFilePath(projectPath), settings);

    private List<ProviderSettings> LoadFromFile(string file)
    {
        if (!File.Exists(file)) return [];
        try
        {
            var read = JsonSerializer.Deserialize<List<ProviderSettings>>(File.ReadAllText(file), JsonOptions) ?? [];
            foreach (var s in read)
            {
                foreach (var key in s.Secrets.Keys.ToList())
                {
                    var cipher = s.Secrets[key];
                    s.Secrets[key] = string.IsNullOrEmpty(cipher) ? string.Empty : secureStorage.Unprotect(cipher);
                }
            }
            return read;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveToFile(string file, IEnumerable<ProviderSettings> settings)
    {
        var copy = settings.Select(s => new ProviderSettings
        {
            Provider = s.Provider,
            Options = new Dictionary<string, string>(s.Options),
            Secrets = s.Secrets.ToDictionary(
                kvp => kvp.Key,
                kvp => string.IsNullOrEmpty(kvp.Value) ? string.Empty : secureStorage.Protect(kvp.Value))
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(copy, JsonOptions));
    }
}
