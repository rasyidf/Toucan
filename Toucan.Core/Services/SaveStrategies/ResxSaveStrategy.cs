using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class ResxSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Resx;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var root = new XElement("root",
                // Standard resx schema headers
                new XElement("resheader", new XAttribute("name", "resmimetype"), new XElement("value", "text/microsoft-resx")),
                new XElement("resheader", new XAttribute("name", "version"), new XElement("value", "2.0")));

            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
            {
                root.Add(new XElement("data",
                    new XAttribute("name", item.Namespace),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", item.Value ?? "")));
            }

            var suffix = language == "default" ? "" : $".{language}";
            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            doc.Save(Path.Combine(path, $"Resources{suffix}.resx"));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
