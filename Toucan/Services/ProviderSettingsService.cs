using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Toucan.Core.Models;

namespace Toucan.Services;

public class ProviderSettingsService : IProviderSettingsService
{
    private readonly ISecureStorageService _secure;

    public ProviderSettingsService(ISecureStorageService secureStorage)
    {
        _secure = secureStorage;
    }

    private static string AppFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "providers.json");

    private static string ProjectFilePath(string projectPath) => Path.Combine(projectPath, ".toucan", "providers.json");

    public IEnumerable<ProviderSettings> LoadAppProviderSettings()
    {
        return LoadFromFile(AppFilePath);
    }

    public void SaveAppProviderSettings(IEnumerable<ProviderSettings> settings)
    {
        SaveToFile(AppFilePath, settings);
    }

    public IEnumerable<ProviderSettings> LoadProjectProviderSettings(string projectPath)
    {
        return LoadFromFile(ProjectFilePath(projectPath));
    }

    public void SaveProjectProviderSettings(string projectPath, IEnumerable<ProviderSettings> settings)
    {
        var file = ProjectFilePath(projectPath);
        var folder = Path.GetDirectoryName(file) ?? projectPath;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        SaveToFile(file, settings);
    }

    private IEnumerable<ProviderSettings> LoadFromFile(string file)
    {
        if (!File.Exists(file)) return Enumerable.Empty<ProviderSettings>();

        try
        {
            var text = File.ReadAllText(file);
            var read = JsonConvert.DeserializeObject<List<ProviderSettings>>(text) ?? new List<ProviderSettings>();

            // decrypt secrets
            foreach (var s in read)
            {
                var keys = s.Secrets.Keys.ToList();
                foreach (var key in keys)
                {
                    var cipher = s.Secrets[key];
                    s.Secrets[key] = _secure.Unprotect(cipher);
                }
            }

            return read;
        }
        catch
        {
            return Enumerable.Empty<ProviderSettings>();
        }
    }

    private void SaveToFile(string file, IEnumerable<ProviderSettings> settings)
    {
        // encrypt secrets before writing
        var copy = settings.Select(s => new ProviderSettings
        {
            Provider = s.Provider,
            Options = new Dictionary<string, string>(s.Options),
            Secrets = s.Secrets.ToDictionary(kvp => kvp.Key, kvp => _secure.Protect(kvp.Value))
        }).ToList();

        var json = JsonConvert.SerializeObject(copy, Formatting.Indented);
        File.WriteAllText(file, json);
    }
}
