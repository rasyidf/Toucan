using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Toucan.Core.Models;

namespace Toucan.Core.Services
{
    internal static class NestedJsonParser
    {
        public static List<TranslationItem> ParseNestedJson(string language, dynamic content, Microsoft.Extensions.Logging.ILogger? logger = null)
        {
            var result = new List<TranslationItem>();
            if (content == null) return result;

            try
            {
                foreach (JProperty property in content)
                {
                    ProcessLanguage(language, result, property);
                }
            }
            catch (System.Exception ex)
            {
                logger?.LogError(ex, "Failed to parse nested JSON for language {language}", language);
            }

            return result;
        }

        private static void ProcessLanguage(string language, List<TranslationItem> list, JToken property)
        {
            if (property.Children().Any())
            {
                foreach (var childProperty in property.Children())
                {
                    ProcessLanguage(language, list, childProperty);
                }
            }
            else
            {
                list.Add(new TranslationItem { Namespace = CleanPath(property.Path), Value = property.ToObject<string>() ?? string.Empty, Language = language });
            }
        }

        private static string CleanPath(string path)
        {
            var newPath = path;
            if (newPath.StartsWith("['")) newPath = newPath.Substring(2);
            if (newPath.EndsWith("']")) newPath = newPath.Substring(0, newPath.Length - 2);
            return newPath;
        }
    }
}
