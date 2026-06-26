using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads .NET .resx/.resw resource files.</summary>
public class ResxLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Resx;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.resx", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.resw", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var lang = DetectLanguage(file);
            try
            {
                var doc = XDocument.Load(file);
                if (doc.Root == null) continue;

                foreach (var data in doc.Root.Elements("data"))
                {
                    var name = data.Attribute("name")?.Value;
                    var value = data.Element("value")?.Value ?? "";
                    if (string.IsNullOrEmpty(name)) continue;
                    // Skip non-string resources (those with type or mimetype attributes)
                    if (data.Attribute("type") != null || data.Attribute("mimetype") != null) continue;
                    items.Add(new TranslationItem { Language = lang, Namespace = name, Value = value });
                }
            }
            catch { }
        }
        return items;
    }

    private static string DetectLanguage(string filePath)
    {
        // Resources.en-US.resx → en-US, Resources.resx → default
        var name = Path.GetFileNameWithoutExtension(filePath); // Resources.en-US
        var parts = name.Split('.');
        if (parts.Length >= 2)
        {
            var candidate = parts[^1];
            if (candidate.Length >= 2 && candidate.Length <= 10) return candidate;
        }
        return "default";
    }
}
