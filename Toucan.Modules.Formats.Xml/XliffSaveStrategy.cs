using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class XliffSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Xliff;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "XLIFF 1.2 and 2.0",
        ["Source text", "notes", "state", "datatype", "original", "extra elements such as context-group", "original file paths"],
        ["Segmentation (<seg-source>, <mrk>) is not edited"]);
    public string DisplayName => "XLIFF";
    public IReadOnlyList<string> FileExtensions => [".xlf", ".xliff"];
    public string DefaultFilePath(string language) => $"{language}.xlf";

    public bool StoresCommentsInline => true;
    public FormatDetection Detection { get; } = new(3, [".xlf", ".xliff"], []);

    private const string Ns12 = "urn:oasis:names:tc:xliff:document:1.2";
    private const string Ns20 = "urn:oasis:names:tc:xliff:document:2.0";

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        var root = Path.GetFullPath(path);
        var primary = context.Languages.FirstOrDefault() ?? "en";

        foreach (var (language, list) in context.LanguageDictionary)
        {
            // Units go back to the file they were loaded from; new ones go to <language>.xlf.
            // A unit with source text stays even when untranslated, so the source is not lost.
            var units = list.ForParse().Where(i => !string.IsNullOrWhiteSpace(i.Value) || HasSource(i));
            foreach (var group in units.GroupBy(i => FileFor(i, language, root)))
                WriteFile(root, group.Key, language, primary, group.OrderBy(i => i.Namespace, StringComparer.Ordinal).ToList());
        }
    }

    private static bool HasSource(TranslationItem item) => item.FormatData?.ContainsKey(XliffFormat.Source) == true;

    private string FileFor(TranslationItem item, string language, string root)
    {
        if (item.FormatData?.TryGetValue(XliffFormat.File, out var rel) == true && !string.IsNullOrEmpty(rel))
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return rel;
        }
        return DefaultFilePath(language);
    }

    private static void WriteFile(string root, string relative, string language, string primary, List<TranslationItem> items)
    {
        var first = items.Select(i => i.FormatData).FirstOrDefault(d => d != null);
        var version = first?.GetValueOrDefault(XliffFormat.Version) ?? "1.2";
        var sourceLang = first?.GetValueOrDefault(XliffFormat.SourceLang) ?? primary;

        var doc = version.StartsWith('2')
            ? Build20(language, sourceLang, items, version)
            : Build12(language, sourceLang, items, version);

        var full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        XmlFile.Save(doc, full);
    }

    private static XDocument Build12(string language, string sourceLang, List<TranslationItem> items, string version)
    {
        XNamespace ns = Ns12;
        var body = new XElement(ns + "body");

        foreach (var item in items)
        {
            var data = item.FormatData;
            var tu = new XElement(ns + "trans-unit", new XAttribute("id", item.Namespace));
            ApplyAttrs(tu, data?.GetValueOrDefault(XliffFormat.UnitAttrs));
            tu.Add(new XElement(ns + "source", data?.GetValueOrDefault(XliffFormat.Source) ?? item.Namespace));

            if (!string.IsNullOrEmpty(item.Value))
            {
                var target = new XElement(ns + "target", item.Value);
                ApplyAttrs(target, data?.GetValueOrDefault(XliffFormat.TargetAttrs));
                if (target.Attribute("state") is { } state && state.Value is "new" or "needs-translation")
                    state.Value = "translated";
                tu.Add(target);
            }

            AddExtra(tu, ns, data);
            foreach (var note in Notes(item)) tu.Add(new XElement(ns + "note", note));
            body.Add(tu);
        }

        var fileEl = new XElement(ns + "file",
            new XAttribute("source-language", sourceLang),
            new XAttribute("target-language", language));
        ApplyAttrs(fileEl, items.Select(i => i.FormatData?.GetValueOrDefault(XliffFormat.FileAttrs)).FirstOrDefault(a => a != null));
        if (fileEl.Attribute("datatype") == null) fileEl.Add(new XAttribute("datatype", "plaintext"));
        fileEl.Add(body);

        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "xliff", new XAttribute("version", version), fileEl));
    }

    private static XDocument Build20(string language, string sourceLang, List<TranslationItem> items, string version)
    {
        XNamespace ns = Ns20;
        var fileEl = new XElement(ns + "file", new XAttribute("id", "f1"));

        foreach (var item in items)
        {
            var data = item.FormatData;
            var unit = new XElement(ns + "unit", new XAttribute("id", item.Namespace));
            ApplyAttrs(unit, data?.GetValueOrDefault(XliffFormat.UnitAttrs));

            var notes = Notes(item).ToList();
            if (notes.Count > 0) unit.Add(new XElement(ns + "notes", notes.Select(n => new XElement(ns + "note", n))));
            AddExtra(unit, ns, data);

            var segment = new XElement(ns + "segment");
            ApplyAttrs(segment, data?.GetValueOrDefault(XliffFormat.SegmentAttrs));
            if (segment.Attribute("state") is { } state && state.Value == "initial" && !string.IsNullOrEmpty(item.Value))
                state.Value = "translated";
            segment.Add(new XElement(ns + "source", data?.GetValueOrDefault(XliffFormat.Source) ?? item.Namespace));
            if (!string.IsNullOrEmpty(item.Value))
            {
                var target = new XElement(ns + "target", item.Value);
                ApplyAttrs(target, data?.GetValueOrDefault(XliffFormat.TargetAttrs));
                segment.Add(target);
            }
            unit.Add(segment);
            fileEl.Add(unit);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "xliff",
                new XAttribute("version", version),
                new XAttribute("srcLang", sourceLang),
                new XAttribute("trgLang", language),
                fileEl));
    }

    private static IEnumerable<string> Notes(TranslationItem item) =>
        string.IsNullOrEmpty(item.Comment) ? [] : item.Comment.Split('\n');

    private static void ApplyAttrs(XElement element, string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var attrs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (attrs == null) return;
        foreach (var (name, value) in attrs) element.SetAttributeValue(XName.Get(name), value);
    }

    private static void AddExtra(XElement parent, XNamespace ns, Dictionary<string, string>? data)
    {
        var raw = data?.GetValueOrDefault(XliffFormat.Extra);
        if (string.IsNullOrEmpty(raw)) return;
        var wrapper = XElement.Parse($"<x xmlns=\"{ns.NamespaceName}\">{raw}</x>");
        foreach (var child in wrapper.Elements()) parent.Add(child);
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
