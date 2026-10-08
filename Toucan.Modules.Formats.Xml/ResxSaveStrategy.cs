using System.IO;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class ResxSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Resx;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        ".resx and .resw string resources",
        ["String entries", "multiline text", "culture suffix in the file name"],
        ["Non-string resources, metadata and custom headers: such projects open with a warning and cannot be saved in place", "Files not named Resources.resx or Resources.<culture>.resx: blocked for the same reason", "Per-entry comments are not kept"]);
    public string DisplayName => "RESX";
    public IReadOnlyList<string> FileExtensions => [".resx"];
    public string DefaultFilePath(string language) => $"Resources{(language == "default" ? "" : $".{language}")}.resx";

    public bool StoresCommentsInline => true;
    public FormatDetection Detection { get; } = new(1, [".resx"], []);

    public IReadOnlyList<string> FindUnsupportedConstructs(string projectRoot)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(projectRoot, "*.resx", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(projectRoot, "*.resw", SearchOption.AllDirectories)))
        {
            var where = Path.GetRelativePath(projectRoot, file);
            if (!IsSavedLocation(projectRoot, file)) found.Add($"{where} is not Resources.resx or Resources.<culture>.resx in the project folder");
            try
            {
                var root = XDocument.Load(file).Root;
                if (root == null) continue;
                if (root.Elements("data").Any(d => d.Attribute("type") != null || d.Attribute("mimetype") != null))
                    found.Add($"non-string resources in {where}");
                if (root.Elements("metadata").Any())
                    found.Add($"<metadata> in {where}");
            }
            catch (System.Xml.XmlException) { }
        }
        return [.. found];
    }

    private static bool IsSavedLocation(string projectRoot, string file) =>
        string.Equals(Path.GetDirectoryName(file), Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(file).Equals(".resx", StringComparison.OrdinalIgnoreCase)
        && Path.GetFileNameWithoutExtension(file).Split('.') is ["Resources", ..];

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
            XmlFile.Save(doc, Path.Combine(path, $"Resources{suffix}.resx"));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
