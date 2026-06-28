// Usage: dotnet run -- <path/to/project.babel> [> toucan.project]
// Converts a .babel XML project file into the toucan.project JSON format.
// Requires: dotnet-script or compile with `dotnet build` (top-level statements, .NET 10+)

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Babel2Toucan <path/to/project.babel>");
    return 2;
}

var filePath = args[0];
if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"File not found: {filePath}");
    return 2;
}

var doc = XDocument.Load(filePath);
var root = doc.Root!;

var project = new Dictionary<string, object?>
{
    ["$schema"] = "./toucan.project.schema.json",
    ["schemaVersion"] = "1.0.0",
    ["projectName"] = NodeText(root, "filename") ?? Path.GetFileNameWithoutExtension(filePath),
    ["beVersion"] = root.Attribute("be_version")?.Value,
    ["framework"] = NodeText(root, "framework"),
};

// Languages
var languages = new List<string>();
var langsNode = root.Element("languages");
if (langsNode is not null)
{
    foreach (var l in langsNode.Elements("language"))
    {
        var code = NodeText(l, "code");
        if (code is not null)
            languages.Add(code);
    }
}
else
{
    // Fall back to scanning <translation> nodes
    foreach (var tr in root.Descendants("translation"))
    {
        var lang = NodeText(tr, "language");
        if (lang is not null && !languages.Contains(lang))
            languages.Add(lang);
    }
}
languages.Sort(StringComparer.Ordinal);
project["languages"] = languages;

// Packages
var packages = new List<Dictionary<string, object?>>();
foreach (var pkg in root.Descendants("package_node"))
{
    packages.Add(ParsePackage(pkg));
}
project["packages"] = packages;

// Translation packages
var translationPackages = new List<Dictionary<string, object?>>();
var tpNode = root.Element("translation_packages");
if (tpNode is not null)
{
    foreach (var tpackage in tpNode.Elements("translation_package"))
    {
        var tname = NodeText(tpackage, "name") ?? "";
        var turls = new List<Dictionary<string, string?>>();
        var urlsNode = tpackage.Element("translation_urls");
        if (urlsNode is not null)
        {
            foreach (var url in urlsNode.Elements("translation_url"))
            {
                turls.Add(new Dictionary<string, string?>
                {
                    ["path"] = NodeText(url, "path"),
                    ["language"] = NodeText(url, "language")
                });
            }
        }
        translationPackages.Add(new Dictionary<string, object?>
        {
            ["name"] = tname,
            ["translationUrls"] = turls
        });
    }
}
project["translationPackages"] = translationPackages;

// Embedded source texts & primary language
var est = NodeText(root, "embedded_source_texts");
project["embeddedSourceTexts"] = string.Equals(est ?? "false", "true", StringComparison.OrdinalIgnoreCase);
project["primaryLanguage"] = NodeText(root, "primary_language");

// Editor configuration
var editorCfg = new Dictionary<string, object>();
var edNode = root.Element("editor_configuration");
if (edNode is not null)
{
    foreach (var c in edNode.Elements())
    {
        if (c.Name.LocalName == "copy_template")
        {
            if (!editorCfg.TryGetValue("copy_templates", out var existing))
            {
                existing = new List<string>();
                editorCfg["copy_templates"] = existing;
            }
            ((List<string>)existing).Add(c.Value);
        }
        else
        {
            editorCfg[c.Name.LocalName] = c.Value;
        }
    }
}
project["editorConfiguration"] = editorCfg;

// Configuration
var cfg = new Dictionary<string, string?>();
var confNode = root.Element("configuration");
if (confNode is not null)
{
    foreach (var c in confNode.Elements())
    {
        cfg[c.Name.LocalName] = c.Value;
    }
}
project["configuration"] = cfg;

// Output
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
Console.WriteLine(JsonSerializer.Serialize(project, options));
return 0;

// --- Helper methods ---

static string? NodeText(XElement parent, string childName)
{
    return parent.Element(childName)?.Value;
}

static Dictionary<string, object?> ParseTranslations(XElement? transElem)
{
    var result = new Dictionary<string, object?>();
    if (transElem is null) return result;

    foreach (var tr in transElem.Elements("translation"))
    {
        var lang = NodeText(tr, "language");
        if (lang is null) continue;

        var approvedText = NodeText(tr, "approved");
        var approved = string.Equals(approvedText, "true", StringComparison.OrdinalIgnoreCase);
        result[lang] = new Dictionary<string, object> { ["approved"] = approved };
    }
    return result;
}

static Dictionary<string, object?> ParseConcept(XElement concept)
{
    return new Dictionary<string, object?>
    {
        ["name"] = NodeText(concept, "name"),
        ["description"] = NodeText(concept, "description") ?? "",
        ["comment"] = NodeText(concept, "comment") ?? "",
        ["translations"] = ParseTranslations(concept.Element("translations"))
    };
}

static Dictionary<string, object?> ParseFolder(XElement node)
{
    var name = NodeText(node, "name");
    var folders = new List<Dictionary<string, object?>>();
    var concepts = new List<Dictionary<string, object?>>();

    var children = node.Element("children");
    if (children is not null)
    {
        foreach (var c in children.Elements())
        {
            if (c.Name.LocalName == "folder_node")
                folders.Add(ParseFolder(c));
            else if (c.Name.LocalName == "concept_node")
                concepts.Add(ParseConcept(c));
        }
    }

    return new Dictionary<string, object?>
    {
        ["name"] = name,
        ["folders"] = folders,
        ["concepts"] = concepts
    };
}

static Dictionary<string, object?> ParseFile(XElement fileNode)
{
    var fileName = NodeText(fileNode, "name");
    var folders = new List<Dictionary<string, object?>>();

    var children = fileNode.Element("children");
    if (children is not null)
    {
        foreach (var ch in children.Elements())
        {
            if (ch.Name.LocalName == "folder_node")
            {
                folders.Add(ParseFolder(ch));
            }
            else if (ch.Name.LocalName == "concept_node")
            {
                // Top-level concept in a file — wrap as a folderless concept
                folders.Add(new Dictionary<string, object?>
                {
                    ["name"] = "",
                    ["folders"] = new List<Dictionary<string, object?>>(),
                    ["concepts"] = new List<Dictionary<string, object?>> { ParseConcept(ch) }
                });
            }
        }
    }

    return new Dictionary<string, object?>
    {
        ["name"] = fileName,
        ["folders"] = folders
    };
}

static Dictionary<string, object?> ParsePackage(XElement pkg)
{
    var name = NodeText(pkg, "name");
    var files = new List<Dictionary<string, object?>>();

    var children = pkg.Element("children");
    if (children is not null)
    {
        foreach (var child in children.Elements())
        {
            if (child.Name.LocalName == "file_node")
                files.Add(ParseFile(child));
        }
    }

    return new Dictionary<string, object?>
    {
        ["name"] = name,
        ["files"] = files
    };
}
