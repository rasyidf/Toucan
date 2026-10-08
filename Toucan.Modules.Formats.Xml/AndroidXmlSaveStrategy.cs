using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class AndroidXmlSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.AndroidXml;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "Android res/values*/strings.xml",
        ["<string> and <string-array> items", "multiline text", "markup characters"],
        ["<plurals>, other resource types and translatable=\"false\" are not kept: such projects open with a warning and cannot be saved in place", "Android escapes (\\' and \\n) are not converted", "XML comments are not kept"]);
    public string DisplayName => "Android XML";
    public IReadOnlyList<string> FileExtensions => [".xml"];
    public string DefaultFilePath(string language) => $"res/{(language == "default" ? "values" : $"values-{language}")}/strings.xml";

    public bool StoresCommentsInline => true;
    public FormatDetection Detection { get; } = new(5, [], ["strings.xml"]);

    public IReadOnlyList<string> FindUnsupportedConstructs(string projectRoot)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var root in new[] { Path.Combine(projectRoot, "res"), projectRoot }.Where(Directory.Exists))
        foreach (var dir in Directory.GetDirectories(root, "values*", SearchOption.TopDirectoryOnly))
        {
            var file = Path.Combine(dir, "strings.xml");
            if (!File.Exists(file)) continue;
            XDocument doc;
            try { doc = XDocument.Load(file); }
            catch (System.Xml.XmlException) { continue; }

            var where = Path.GetRelativePath(projectRoot, file);
            foreach (var el in doc.Root?.Elements() ?? [])
            {
                if (el.Name.LocalName is not ("string" or "string-array"))
                    found.Add($"<{el.Name.LocalName}> in {where}");
                else if (el.Name.LocalName == "string" && (string?)el.Attribute("translatable") == "false")
                    found.Add($"translatable=\"false\" in {where}");
            }
        }
        return [.. found];
    }

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
            XmlFile.Save(doc, Path.Combine(dir, "strings.xml"));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
