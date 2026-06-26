using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class XliffSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Xliff;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        var sourceLang = context.Languages.FirstOrDefault() ?? "en";

        foreach (var (language, list) in context.LanguageDictionary)
        {
            XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
            var body = new XElement(ns + "body");

            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
            {
                body.Add(new XElement(ns + "trans-unit",
                    new XAttribute("id", item.Namespace),
                    new XElement(ns + "source", item.Namespace),
                    new XElement(ns + "target", item.Value ?? "")));
            }

            var fileEl = new XElement(ns + "file",
                new XAttribute("source-language", sourceLang),
                new XAttribute("target-language", language),
                new XAttribute("datatype", "plaintext"),
                body);

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(ns + "xliff", new XAttribute("version", "1.2"), fileEl));

            doc.Save(Path.Combine(path, $"{language}.xlf"));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
