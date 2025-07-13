
using System.IO;
using System.Text.Json;
using Toucan.Contracts.Services;
using Toucan.Models;

namespace Toucan.Services;
public class AppConfigService : IAppConfigService
{
    private const string FileName = "AppConfig.json";
    private readonly string _configPath;
    public AppConfig Current { get; private set; }

    public AppConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, FileName);
        Reload();
    }

    public void Reload()
    {
        if (File.Exists(_configPath))
        {
            var json = File.ReadAllText(_configPath);
            Current = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        else
        {
            Current = new AppConfig();
            Save(); // create default file
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }
}
