using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads .NET .resx/.resw resource files.</summary>
public class ResxLoadStrategy : ILoadStrategy, IResourceLoadStrategy
{
    public string FormatId => FormatIds.Resx;

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

    public ResourceReadResult ReadResource(ResourceReadRequest request)
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Resource.RelativePath);
        var content = ResourceText.Read(request, out var encoding);
        using var reader = System.Xml.XmlReader.Create(new StringReader(content), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Name != "root") throw new FormatException($"{request.Resource.RelativePath} [root]: RESX needs a root element.");
        var entries = xml.Root.Elements("data").Where(d => d.Attribute("type") == null && d.Attribute("mimetype") == null)
            .Select(d => new TranslationItem { Language = request.Resource.Language,
                Namespace = (string?)d.Attribute("name") ?? throw new FormatException($"{request.Resource.RelativePath} [name]: missing resource name."),
                Value = (string?)d.Element("value") ?? "", Comment = (string?)d.Element("comment") ?? "" }).ToList();
        var result = ResourceFileAdapter.Bind(request, entries, true);
        result.Document!.DetectedOptions = ResourceText.Detect(content, encoding);
        result.Document.Data["resx"] = System.Text.Json.JsonSerializer.SerializeToElement(xml.ToString());
        return result;
    }

    private static readonly Regex s_culturePattern = new("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string DetectLanguage(string filePath)
    {
        // Resources.en-US.resx → en-US, Resources.resx → default. Views.Home.Index.resx and
        // Resources.Designer.resx are not languages: the last part must be a real culture name.
        var name = Path.GetFileNameWithoutExtension(filePath); // Resources.en-US
        var parts = name.Split('.');
        if (parts.Length >= 2 && IsCultureName(parts[^1])) return parts[^1];
        return "default";
    }

    private static bool IsCultureName(string candidate)
    {
        if (!s_culturePattern.IsMatch(candidate)) return false;

        // Invariant-globalization builds have no culture data to check against; the pattern is all we have.
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant) return true;
        try
        {
            CultureInfo.GetCultureInfo(candidate, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
