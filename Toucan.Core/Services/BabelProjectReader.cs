using System.Xml;
using System.Xml.Linq;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class BabelProjectReader
{
    public static ProjectSettings Read(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "babeledit_project") throw new FormatException("This is not a BabelEdit project.");
        static string Text(XElement? e, string tag) => e?.Element(tag)?.Value ?? string.Empty;
        var framework = Text(root, "framework");
        var format = framework switch
        {
            "generic-yaml" or "rails-yaml" or "ember-yaml" => FormatIds.Yaml,
            "prop-files" => FormatIds.JavaProperties,
            "arb" => FormatIds.Arb,
            "laravel" => FormatIds.LaravelPhp,
            "resx" => FormatIds.Resx,
            "xliff" => FormatIds.Xliff,
            "gettext" or "po" => FormatIds.Po,
            "generic-json" or "ngx-translate" or "angular-transloco" or "vue-json" or "i18next" or
                "react-json" or "svelte-i18n" or "ember-json" or "laravel-json" => FormatIds.Json,
            _ => throw new FormatException($"BabelEdit framework '{framework}' is not supported for import.")
        };
        var data = new BabelImportData { SourceFile = Path.GetFileName(file) };
        foreach (var c in root.Element("configuration")?.Elements() ?? []) data.Configuration[c.Name.LocalName] = c.Value;
        if (format == FormatIds.Json) data.JsonLayout = data.Configuration.GetValueOrDefault("format") is "json" or "plaintext-json" ? "flat" : "nested";
        foreach (var package in root.Descendants("package_node"))
            Walk(package, Text(package, "name"), string.Empty, data.Concepts);
        var settings = new ProjectSettings
        {
            ProjectPath = Path.GetDirectoryName(Path.GetFullPath(file))!,
            Name = Path.GetFileNameWithoutExtension(file), Framework = framework, SaveFormat = format,
            BabelImport = data, PrimaryLanguage = Text(root, "primary_language"),
            Languages = root.Element("languages")?.Elements("language").Select(l => Text(l, "code")).Where(c => c.Length > 0).ToList() ?? [],
            SaveEmptyTranslations = !bool.TryParse(Text(root.Element("editor_configuration"), "save_empty_translations"), out var keep) || keep,
            TranslationOrder = Text(root.Element("editor_configuration"), "translation_order"),
            CopyTemplates = root.Element("editor_configuration")?.Element("copy_templates")?.Elements("copy_template").Select(c => c.Value).ToList() ?? []
        };
        if (settings.PrimaryLanguage.Length == 0) settings.PrimaryLanguage = settings.Languages.FirstOrDefault() ?? "en-US";
        if (format == FormatIds.JavaProperties) settings.InputEncoding = data.Configuration.GetValueOrDefault("encoding");
        foreach (var p in root.Element("translation_packages")?.Elements("translation_package") ?? [])
        {
            var package = new TranslationPackage { Name = Text(p, "name") };
            foreach (var url in p.Element("translation_urls")?.Elements("translation_url") ?? [])
            {
                var path = Text(url, "path");
                var language = Text(url, "language");
                if (path.Length == 0 || language.Length == 0) throw new FormatException("A BabelEdit language file has no path or language.");
                ResolvePath(settings.ProjectPath, path);
                package.TranslationUrls.Add(new TranslationUrl { Language = language, Path = path });
            }
            settings.TranslationPackages.Add(package);
        }
        return settings;
    }

    public static string ResolvePath(string root, string path)
    {
        var target = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Imported resource paths must stay inside the project folder. Copy external resource files into the project before importing.");
        return target;
    }

    private static void Walk(XElement node, string package, string prefix, List<BabelConcept> concepts)
    {
        foreach (var child in node.Element("children")?.Elements() ?? [])
        {
            var name = child.Element("name")?.Value ?? string.Empty;
            var key = string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
            if (child.Name == "concept_node")
                concepts.Add(new BabelConcept
                {
                    Package = package, Key = key, Description = child.Element("description")?.Value ?? string.Empty,
                    Comment = child.Element("comment")?.Value ?? string.Empty,
                    Approved = (child.Element("translations")?.Elements("translation") ?? [])
                        .Where(t => t.Element("language") != null)
                        .ToDictionary(t => t.Element("language")!.Value, t => (bool?)t.Element("approved") ?? false)
                });
            else if (child.Name == "folder_node" || child.Name == "file_node")
                Walk(child, package, key, concepts);
        }
    }

    public static void ApplyMetadata(ProjectSettings settings, IEnumerable<TranslationItem> items)
    {
        if (settings.BabelImport == null) return;
        foreach (var item in items)
        {
            var package = item.FormatData?.GetValueOrDefault("babel.package") ?? "main";
            var key = item.FormatData?.GetValueOrDefault("babel.key") ?? item.Namespace;
            var concept = settings.BabelImport.Concepts.FirstOrDefault(c => c.Key == key && c.Package == package);
            if (concept == null) continue;
            item.Comment = concept.Comment;
            item.IsApproved = concept.Approved.GetValueOrDefault(item.Language);
            (item.FormatData ??= new())["babel.description"] = concept.Description;
        }
    }

    public static void CaptureMetadata(ProjectSettings settings, IEnumerable<TranslationItem> items)
    {
        if (settings.BabelImport == null) return;
        settings.BabelImport.Concepts = items.GroupBy(i => (Package: i.FormatData?.GetValueOrDefault("babel.package") ?? "main", Key: i.FormatData?.GetValueOrDefault("babel.key") ?? i.Namespace))
            .Select(g => new BabelConcept { Package = g.Key.Package, Key = g.Key.Key, Comment = g.First().Comment,
                Description = g.First().FormatData?.GetValueOrDefault("babel.description") ?? string.Empty,
                Approved = g.ToDictionary(i => i.Language, i => i.IsApproved) }).ToList();
    }
}
