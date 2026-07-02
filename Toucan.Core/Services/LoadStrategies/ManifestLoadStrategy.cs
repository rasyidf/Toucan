using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class ManifestLoadStrategy(IFileService fileService, ILogger<ManifestLoadStrategy> logger) : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Json;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var manifestPath = Path.Combine(folder, "toucan.tproj");
        if (!File.Exists(manifestPath)) return [];

        var text = fileService.ReadText(folder, "toucan.tproj");
        if (string.IsNullOrWhiteSpace(text)) return [];

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!root.TryGetProperty("translationPackages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<TranslationItem>();

            foreach (var pkg in packages.EnumerateArray())
            {
                if (!pkg.TryGetProperty("translationUrls", out var urls) || urls.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var u in urls.EnumerateArray())
                {
                    var language = u.TryGetProperty("language", out var langEl) ? langEl.GetString() : null;
                    var path = u.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                    if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(path)) continue;

                    var filePath = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(filePath)) continue;

                    var content = fileService.ReadText(Path.GetDirectoryName(filePath)!, Path.GetFileName(filePath));
                    results.AddRange(NestedJsonParser.ParseNestedJson(language, content, logger));
                }
            }

            return results;
        }
        catch { return []; }
    }
}
