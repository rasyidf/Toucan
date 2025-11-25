using System.IO;
using Newtonsoft.Json.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies
{
    public class ManifestLoadStrategy : ILoadStrategy
    {
        public SaveStyles Style => SaveStyles.Json; // associated style is not strictly relevant; loader is based on manifest

        private readonly IFileService _fileService;
        private readonly Microsoft.Extensions.Logging.ILogger<ManifestLoadStrategy> _logger;
 
        public ManifestLoadStrategy(IFileService fileService, Microsoft.Extensions.Logging.ILogger<ManifestLoadStrategy> logger)
        {
            _fileService = fileService;
            _logger = logger;
        }

        public IEnumerable<TranslationItem> Load(string folder)
        {
            var manifestName = "toucan.project";
            var manifestPath = Path.Combine(folder, manifestName);
            if (!File.Exists(manifestPath))
                return Enumerable.Empty<TranslationItem>();

            var text = _fileService.ReadText(folder, manifestName);
            if (string.IsNullOrEmpty(text)) return Enumerable.Empty<TranslationItem>();

            try
            {
                var root = JObject.Parse(text);
                var packages = root["translationPackages"] as JArray;
                if (packages == null) return Enumerable.Empty<TranslationItem>();

                var results = new List<TranslationItem>();
                foreach (var pkg in packages)
                {
                    var urls = pkg["translationUrls"] as JArray;
                    if (urls == null) continue;

                    foreach (var u in urls)
                    {
                        var language = (string)u["language"];
                        var path = (string)u["path"];
                        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(path)) continue;

                        // Normalize path (relative to project root)
                        var filePath = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(filePath)) continue;

                            var content = _fileService.Read<dynamic>(Path.GetDirectoryName(filePath), Path.GetFileName(filePath));
                        results.AddRange(NestedJsonParser.ParseNestedJson(language, content, _logger));
                    }
                }
                return results;
            }
            catch
            {
                return Enumerable.Empty<TranslationItem>();
            }
        }
    }
}
