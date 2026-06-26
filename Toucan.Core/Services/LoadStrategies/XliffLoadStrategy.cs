using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads XLIFF 1.2 and 2.0 translation files.</summary>
public class XliffLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Xliff;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.xlf", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.xliff", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            try { items.AddRange(ParseXliff(file)); }
            catch { }
        }
        return items;
    }

    private static List<TranslationItem> ParseXliff(string filePath)
    {
        var result = new List<TranslationItem>();
        var doc = XDocument.Load(filePath);
        if (doc.Root == null) return result;

        var ns = doc.Root.GetDefaultNamespace();

        // XLIFF 1.2: <file target-language="xx"><body><trans-unit id="key"><target>...</target>
        foreach (var fileEl in doc.Root.Elements(ns + "file"))
        {
            var targetLang = fileEl.Attribute("target-language")?.Value ?? fileEl.Attribute("target")?.Value ?? "unknown";
            var body = fileEl.Element(ns + "body") ?? fileEl.Element(ns + "group");
            if (body == null) continue;

            foreach (var tu in body.Descendants(ns + "trans-unit"))
            {
                var id = tu.Attribute("id")?.Value ?? "";
                var target = tu.Element(ns + "target")?.Value ?? tu.Element(ns + "source")?.Value ?? "";
                if (!string.IsNullOrEmpty(id))
                    result.Add(new TranslationItem { Language = targetLang, Namespace = id, Value = target });
            }
        }

        // XLIFF 2.0: <file><unit id="key"><segment><target>...</target>
        foreach (var unit in doc.Root.Descendants(ns + "unit"))
        {
            var id = unit.Attribute("id")?.Value ?? "";
            var segment = unit.Element(ns + "segment");
            var targetLang = doc.Root.Attribute("trgLang")?.Value ?? "unknown";
            var target = segment?.Element(ns + "target")?.Value ?? segment?.Element(ns + "source")?.Value ?? "";
            if (!string.IsNullOrEmpty(id))
                result.Add(new TranslationItem { Language = targetLang, Namespace = id, Value = target });
        }

        return result;
    }
}
