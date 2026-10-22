using System.Xml;
using System.Text.Json;
using Toucan.Core.Contracts.Services;
using System.Xml.Linq;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class BabelProjectReader
{
    public static CompatibilityProject Read(string file)
    {
        try { return ReadProject(file); }
        catch (XmlException ex) { throw Error(file, "xml", ex.Message); }
        catch (ArgumentException ex) { throw Error(file, "mapping", ex.Message); }
    }

    private static CompatibilityProject ReadProject(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "babeledit_project") throw Error(file, "root", "Expected babeledit_project root.");
        static string Text(XElement? e, string tag) => e?.Element(tag)?.Value ?? string.Empty;
        var version = (string?)root.Attribute("version");
        if (version is not (null or "1.3")) throw Error(file, "version", $"Unsupported project schema '{version}'.");
        var framework = Text(root, "framework");
        if (framework.Length == 0) throw Error(file, "framework", "A framework is required.");
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
            _ => throw Error(file, "framework", $"Framework '{framework}' is not supported for import.")
        };
        var data = new ImportedProjectMetadata { SourceFile = Path.GetFileName(file) };
        foreach (var c in root.Element("configuration")?.Elements() ?? []) data.Configuration[c.Name.LocalName] = c.Value;
        if (format == FormatIds.Json) data.JsonLayout = data.Configuration.GetValueOrDefault("format") is "json" or "plaintext-json" ? "flat" : "nested";
        foreach (var package in root.Descendants("package_node"))
            Walk(package, Text(package, "name"), string.Empty, data.Concepts);
        var settings = new CompatibilityProject
        {
            ProjectPath = Path.GetDirectoryName(Path.GetFullPath(file))!,
            Name = Path.GetFileNameWithoutExtension(file), Framework = framework, SaveFormat = format,
            PrimaryLanguage = Text(root, "primary_language"),
            Languages = root.Element("languages")?.Elements("language").Select(l => Text(l, "code")).Where(c => c.Length > 0).ToList() ?? [],
            SaveEmptyTranslations = !bool.TryParse(Text(root.Element("editor_configuration"), "save_empty_translations"), out var keep) || keep,
            TranslationOrder = Text(root.Element("editor_configuration"), "translation_order"),
            CopyTemplates = root.Element("editor_configuration")?.Element("copy_templates")?.Elements("copy_template").Select(c => c.Value).ToList() ?? []
        };
        if (settings.PrimaryLanguage.Length == 0) settings.PrimaryLanguage = settings.Languages.FirstOrDefault() ?? "en-US";
        if (format == FormatIds.JavaProperties) settings.InputEncoding = data.Configuration.GetValueOrDefault("encoding");
        settings.ResourceOptions = new ResourceOptions
        {
            Layout = data.JsonLayout == "flat" ? ResourceLayout.Literal : format == FormatIds.Json ? ResourceLayout.Nested : null,
            Delimiter = ".", InputEncoding = settings.InputEncoding, SaveEmptyTranslations = settings.SaveEmptyTranslations,
            Indent = data.Configuration.GetValueOrDefault("indent") switch
                { "none" => ResourceIndent.None, "space2" => ResourceIndent.Space2, "space4" => ResourceIndent.Space4, "tab" => ResourceIndent.Tab, _ => null },
            Order = settings.TranslationOrder switch { "alphabetically" or "alphabetical" => ResourceOrder.Alphabetical, "file" => ResourceOrder.Preserve, _ => null },
            Arrays = bool.TryParse(data.Configuration.GetValueOrDefault("support_arrays"), out var arrays) ? arrays ? ResourceArrayPolicy.ContiguousIndexes : ResourceArrayPolicy.Disabled : null
        };
        foreach (var p in root.Element("translation_packages")?.Elements("translation_package") ?? [])
        {
            var package = new TranslationPackage { Name = Text(p, "name") };
            foreach (var url in p.Element("translation_urls")?.Elements("translation_url") ?? [])
            {
                var path = Text(url, "path");
                var language = Text(url, "language");
                if (path.Length == 0 || language.Length == 0) throw Error(path.Length == 0 ? file : path, path.Length == 0 ? "path" : "language", "A resource URL needs a path and a language.");
                ResolvePath(settings.ProjectPath, path);
                package.TranslationUrls.Add(new TranslationUrl { Language = language, Path = path });
            }
            settings.TranslationPackages.Add(package);
        }
        if (settings.TranslationPackages.Count == 0 || settings.TranslationPackages.Any(p => p.TranslationUrls.Count == 0))
            throw Error(file, "translation_packages", "Each project needs packages with explicit resource URLs.");
        if (settings.TranslationPackages.Count == 1 && settings.TranslationPackages[0].Name.Length == 0)
            settings.TranslationPackages[0].Name = "main"; // Legacy versionless single-package projects.
        if (settings.Languages.Any(string.IsNullOrWhiteSpace) || settings.Languages.Distinct(StringComparer.Ordinal).Count() != settings.Languages.Count)
            throw Error(file, "languages", "Declared languages must be nonempty and unique.");
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var package in settings.TranslationPackages)
        {
            if (package.Name.Length == 0 || !packages.Add(package.Name)) throw Error(file, "translation_package.name", "Package names must be nonempty and unique.");
            var languages = new HashSet<string>(StringComparer.Ordinal);
            foreach (var url in package.TranslationUrls)
            {
                if (!languages.Add(url.Language)) throw Error(url.Path, "language", "Duplicate package/language mapping.");
                var resolved = ResolvePath(settings.ProjectPath, url.Path);
                if (!paths.Add(resolved)) throw Error(url.Path, "path", "A resource cannot have multiple mappings.");
                if (!File.Exists(resolved)) throw Error(url.Path, "path", "The declared resource file does not exist.");
                if (settings.Languages.Count > 0 && !settings.Languages.Contains(url.Language)) throw Error(url.Path, "language", "Resource locale is absent from the declared language list.");
                settings.ResourceDocuments.Add(new ResourceDocumentState { Resource = new ResourceDescriptor
                {
                    Id = url.Path.Replace('\\', '/'), RelativePath = url.Path.Replace('\\', '/'), Language = url.Language,
                    Package = package.Name, FormatId = format, Dialect = framework,
                    SelectorPrefix = settings.TranslationPackages.Count > 1 ? ResourceEntryOperations.Escape(package.Name) + "." : "",
                    Options = new ResourceOptions()
                }});
            }
        }
        if (settings.Languages.Count == 0) settings.Languages = settings.ResourceDocuments.Select(d => d.Resource.Language).Distinct().ToList();
        if (!settings.Languages.Contains(settings.PrimaryLanguage)) settings.PrimaryLanguage = settings.Languages[0];
        var envelope = new CompatibilityEnvelope { Source = "babeledit" };
        envelope.Data["project"] = JsonSerializer.SerializeToElement(data);
        settings.Compatibility["babeledit"] = envelope;
        return settings;
    }

    public static string ResolvePath(string root, string path)
    {
        try { return ResourceFilePath.Resolve(root, path); }
        catch (ArgumentException ex) { throw Error(path, "path", ex.Message); }
    }

    private static CompatibilityImportException Error(string path, string field, string message) =>
        new([new ResourceDiagnostic("compatibility.invalid-project", message, ResourceDiagnosticSeverity.Error, path, field)]);

    private static void Walk(XElement node, string package, string prefix, List<ImportedConcept> concepts)
    {
        foreach (var child in node.Element("children")?.Elements() ?? [])
        {
            var name = child.Element("name")?.Value ?? string.Empty;
            var key = string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;
            if (child.Name == "concept_node")
                concepts.Add(new ImportedConcept
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

    public static void ApplyMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items)
    {
        var import = Metadata(compatibility);
        if (import is null) return;
        foreach (var item in items)
        {
            var package = item.Identity?.Package ?? item.FormatData?.GetValueOrDefault("babel.package") ?? "main";
            var key = item.Identity is { } identity ? ResourceEntryOperations.Selector(identity.Path) : item.FormatData?.GetValueOrDefault("babel.key") ?? item.Namespace;
            var concept = import.Concepts.FirstOrDefault(c => c.Key == key && c.Package == package);
            if (concept == null) continue;
            item.Comment = concept.Comment;
            item.IsApproved = concept.Approved.GetValueOrDefault(item.Language);
            (item.FormatData ??= new())["babel.description"] = concept.Description;
        }
    }

    public static void CaptureMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items)
    {
        var import = Metadata(compatibility);
        if (import is null) return;
        import.Concepts = items.GroupBy(i => (Package: i.Identity?.Package ?? i.FormatData?.GetValueOrDefault("babel.package") ?? "main", Key: i.Identity is { } identity ? ResourceEntryOperations.Selector(identity.Path) : i.FormatData?.GetValueOrDefault("babel.key") ?? i.Namespace))
            .Select(g => new ImportedConcept { Package = g.Key.Package, Key = g.Key.Key, Comment = g.First().Comment,
                ExtensionData = import.Concepts.FirstOrDefault(c => c.Package == g.Key.Package && c.Key == g.Key.Key)?.ExtensionData,
                Description = g.First().FormatData?.GetValueOrDefault("babel.description") ?? string.Empty,
                Approved = g.ToDictionary(i => i.Language, i => i.IsApproved) }).ToList();
        compatibility["babeledit"].Data["project"] = JsonSerializer.SerializeToElement(import);
    }
    private static ImportedProjectMetadata? Metadata(IDictionary<string, CompatibilityEnvelope> compatibility) =>
        compatibility.TryGetValue("babeledit", out var envelope) && envelope.Version == 1 && envelope.Data.TryGetValue("project", out var data)
            ? data.Deserialize<ImportedProjectMetadata>() : null;
}
