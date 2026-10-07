using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Persists translation provider settings app-wide (~/Documents/Toucan/providers.json) or per project
/// (&lt;project&gt;/.toucan/providers.json). Only options and the names of secret fields are written there; secret values
/// go to <see cref="ISecretService"/> (<c>mt/&lt;provider&gt;/&lt;field&gt;</c>, or <c>project/&lt;id&gt;/mt/…</c> for a project),
/// so a project folder never holds an API key. Files written by older versions kept encrypted secrets inline: those are
/// still read, and moved to the secret store the next time the settings are saved.
/// </summary>
public sealed class ProviderSettingsService(ISecretService secrets, ISecureStorageService legacyProtector, string? appFile = null) : IProviderSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string AppFilePath => appFile ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "providers.json");

    private static string ProjectFilePath(string projectPath) => Path.Combine(projectPath, ".toucan", "providers.json");

    public IEnumerable<ProviderSettings> LoadAppProviderSettings() => LoadFromFile(AppFilePath, null);

    public void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings) => SaveToFile(AppFilePath, null, settings);

    public IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath) => LoadFromFile(ProjectFilePath(projectPath), projectPath);

    public void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings) =>
        SaveToFile(ProjectFilePath(projectPath), projectPath, settings);

    private List<ProviderSettings> LoadFromFile(string file, string? projectPath)
    {
        if (!File.Exists(file)) return [];
        try
        {
            var read = JsonSerializer.Deserialize<List<ProviderSettings>>(File.ReadAllText(file), JsonOptions) ?? [];
            foreach (var s in read)
            {
                foreach (var key in s.Secrets.Keys.ToList())
                {
                    var stored = secrets.GetSecret(SecretKeys.Provider(s.Provider, key, projectPath));
                    var inline = s.Secrets[key];
                    s.Secrets[key] = stored ?? (string.IsNullOrEmpty(inline) ? string.Empty : legacyProtector.Unprotect(inline));
                }
            }
            return read;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveToFile(string file, string? projectPath, IEnumerable<ProviderSettings> settings)
    {
        var list = settings.ToList();
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in list)
        {
            foreach (var (field, value) in s.Secrets)
            {
                var key = SecretKeys.Provider(s.Provider, field, projectPath);
                secrets.SetSecret(key, value);
                if (!string.IsNullOrEmpty(value)) keep.Add(key);
            }
        }

        // Secrets of providers or fields removed in the dialog go too.
        foreach (var stale in secrets.Keys(SecretKeys.ProviderScope(projectPath)).Where(k => !keep.Contains(k)))
            secrets.Remove(stale);

        var copy = list.Select(s => new ProviderSettings
        {
            Provider = s.Provider,
            Options = new Dictionary<string, string>(s.Options),
            Secrets = s.Secrets.Keys.ToDictionary(k => k, _ => string.Empty)
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(copy, JsonOptions));
    }
}
