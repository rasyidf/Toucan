using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads Android res/values-{lang}/strings.xml files.</summary>
public class AndroidXmlLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.AndroidXml;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var items = new List<TranslationItem>();

        // Look for values/ and values-XX/ directories
        var resDir = Directory.Exists(Path.Combine(folder, "res")) ? Path.Combine(folder, "res") : folder;
        var dirs = Directory.GetDirectories(resDir, "values*", SearchOption.TopDirectoryOnly);

        foreach (var dir in dirs)
        {
            var dirName = Path.GetFileName(dir);
            var lang = dirName == "values" ? "default" : dirName.Replace("values-", "");

            var stringsFile = Path.Combine(dir, "strings.xml");
            if (!File.Exists(stringsFile)) continue;

            try
            {
                var doc = XDocument.Load(stringsFile);
                if (doc.Root == null) continue;

                foreach (var el in doc.Root.Elements("string"))
                {
                    var name = el.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    items.Add(new TranslationItem { Language = lang, Namespace = name, Value = el.Value });
                }

                // string-array support
                foreach (var arr in doc.Root.Elements("string-array"))
                {
                    var name = arr.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    int idx = 0;
                    foreach (var item in arr.Elements("item"))
                        items.Add(new TranslationItem { Language = lang, Namespace = $"{name}[{idx++}]", Value = item.Value });
                }
            }
            catch { }
        }
        return items;
    }
}
