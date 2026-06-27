using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

public class ProviderSettingsService : IProviderSettingsService
{
    private readonly ISecureStorageService _secure;

    public ProviderSettingsService(ISecureStorageService secureStorage) => _secure = secureStorage;

    private static string AppFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "providers.json");

    private static string ProjectFilePath(string projectPath) => Path.Combine(projectPath, ".toucan", "providers.json");

    public IEnumerable<ProviderSettings> LoadAppProviderSettings() => LoadFromFile(AppFilePath);
    public void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings) => SaveToFile(AppFilePath, settings);
    public IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath) => LoadFromFile(ProjectFilePath(projectPath));

    public void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings)
    {
        var file = ProjectFilePath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        SaveToFile(file, settings);
    }

    private IEnumerable<ProviderSettings> LoadFromFile(string file)
    {
        if (!File.Exists(file)) return [];
        try
        {
            var read = JsonConvert.DeserializeObject<List<ProviderSettings>>(File.ReadAllText(file)) ?? [];
            foreach (var s in read)
                foreach (var key in s.Secrets.Keys.ToList())
                    s.Secrets[key] = _secure.Unprotect(s.Secrets[key]);
            return read;
        }
        catch { return []; }
    }

    private void SaveToFile(string file, IEnumerable<ProviderSettings> settings)
    {
        var copy = settings.Select(s => new ProviderSettings
        {
            Provider = s.Provider,
            Options = new Dictionary<string, string>(s.Options),
            Secrets = s.Secrets.ToDictionary(kvp => kvp.Key, kvp => _secure.Protect(kvp.Value))
        }).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonConvert.SerializeObject(copy, Formatting.Indented));
    }
}
