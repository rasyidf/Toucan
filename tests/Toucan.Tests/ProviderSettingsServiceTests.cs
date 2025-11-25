using System.IO;
using System.Linq;
using Toucan.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Tests;

public class ProviderSettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_ProjectSettings_PreservesSecrets()
    {
        var secure = new SecureStorageService();
        var svc = new ProviderSettingsService(secure);

        var projectDir = Path.Combine(Path.GetTempPath(), "toucan_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(projectDir);

        var settings = new[]
        {
            new ProviderSettings
            {
                Provider = "DeepL",
                Options = { ["endpoint"] = "https://api.deepl.com/v2/translate" },
                Secrets = { ["api_key"] = "secret-value-123" }
            }
        };

        svc.SaveProjectProviderSettings(projectDir, settings);

        var loaded = svc.LoadProjectProviderSettings(projectDir).ToList();

        Assert.Single(loaded);
        Assert.Equal("DeepL", loaded[0].Provider);
        Assert.True(loaded[0].Secrets.ContainsKey("api_key"));
        Assert.Equal("secret-value-123", loaded[0].Secrets["api_key"]);
    }
}
