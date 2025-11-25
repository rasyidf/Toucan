using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies
{
    public class JsonLoadStrategy : ILoadStrategy
    {
        public SaveStyles Style => SaveStyles.Json;

            private readonly IFileService _fileService;
            private readonly Microsoft.Extensions.Logging.ILogger<JsonLoadStrategy> _logger;

            public JsonLoadStrategy(IFileService fileService, Microsoft.Extensions.Logging.ILogger<JsonLoadStrategy> logger)
        {
            _fileService = fileService;
                _logger = logger;
        }

        public IEnumerable<TranslationItem> Load(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return Enumerable.Empty<TranslationItem>();
                // Scan files recursively to support nested locales folder structure
                var files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
            var items = new List<TranslationItem>();
            foreach (var filePath in files)
            {
                var filename = Path.GetFileName(filePath);
                    // Determine language and namespace prefix from file path
                    var (lang, prefix) = GetLanguageAndPrefix(folder, filePath);
                    var folderOfFile = Path.GetDirectoryName(filePath) ?? folder;
                    var content = _fileService.Read<dynamic>(folderOfFile, filename);
                    var parsed = NestedJsonParser.ParseNestedJson(lang, content, _logger);

                    // Apply prefix (file-based namespace) if present
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        foreach (var p in parsed)
                        {
                            p.Namespace = string.IsNullOrWhiteSpace(p.Namespace) ? prefix : $"{prefix}.{p.Namespace}";
                        }
                    }
                    items.AddRange(parsed);
                if (items.Count == 0)
                {
                    items.Add(new TranslationItem() { Language = lang });
                }
            }

            return items;
        }

        private static (string language, string prefix) GetLanguageAndPrefix(string rootFolder, string filePath)
        {
            // Calculate relative path segments
            var relative = Path.GetRelativePath(rootFolder, filePath);
            var sep = Path.DirectorySeparatorChar;
            var segments = relative.Split(sep, StringSplitOptions.RemoveEmptyEntries);
            // If only the file is present
            if (segments.Length == 1)
            {
                var language = Path.GetFileNameWithoutExtension(segments[0]);
                return (language, string.Empty);
            }

            // Helper to detect 'locales' folder
            var localesIdx = Array.FindIndex(segments, s => s.Equals("locales", StringComparison.InvariantCultureIgnoreCase));
            int languageIndex = -1;
            if (localesIdx >= 0 && localesIdx < segments.Length - 1)
            {
                languageIndex = localesIdx + 1;
            }
            else if (IsLanguageCandidate(segments[0]))
            {
                languageIndex = 0;
            }
            else if (segments.Length >= 2 && IsLanguageCandidate(segments[^2]))
            {
                languageIndex = segments.Length - 2;
            }
            else
            {
                // default: take parent folder as language if available, else file base
                languageIndex = Math.Max(0, segments.Length - 2);
            }

            var languageSegment = segments[languageIndex];
            var lang = Path.GetFileNameWithoutExtension(languageSegment);

            // Determine prefix as segments after languageIndex up to file (inclusive of file base name)
            var prefixSegments = new List<string>();
            for (int i = languageIndex + 1; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (i == segments.Length - 1) // last = file
                {
                    seg = Path.GetFileNameWithoutExtension(seg);
                }
                prefixSegments.Add(seg);
            }
            var prefix = prefixSegments.Count == 0 ? string.Empty : string.Join('.', prefixSegments.Select(s => s.Trim()));
            return (lang, prefix);
        }

        private static bool IsLanguageCandidate(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return false;
            // Lower length languages, allow 'en', 'en-US', 'fr', 'pt-BR' - basic heuristic
            // Match letters and optional hyphen-digit/letters
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(segment, "^[a-z]{2}(-[A-Za-z0-9]+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return segment.Length <= 5;
            }
        }

        
    }
}
