using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class AndroidXmlSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.AndroidXml;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var dirName = language == "default" ? "values" : $"values-{language}";
            var dir = Path.Combine(path, "res", dirName);
            Directory.CreateDirectory(dir);

            var resources = new XElement("resources");
            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
            {
                if (item.Namespace.Contains('['))
                {
                    // Array item — group later
                    continue;
                }
                resources.Add(new XElement("string", new XAttribute("name", item.Namespace), item.Value ?? ""));
            }

            // Handle string-arrays
            var arrays = list.NoEmpty()
                .Where(i => i.Namespace.Contains('['))
                .GroupBy(i => i.Namespace[..i.Namespace.IndexOf('[')])
                .OrderBy(g => g.Key);

            foreach (var arr in arrays)
            {
                var arrEl = new XElement("string-array", new XAttribute("name", arr.Key));
                foreach (var item in arr.OrderBy(i => i.Namespace))
                    arrEl.Add(new XElement("item", item.Value ?? ""));
                resources.Add(arrEl);
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), resources);
            doc.Save(Path.Combine(dir, "strings.xml"));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
