using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>
/// Loads XLIFF 1.2 and 2.0 translation files. The key is the unit id, the value is the target text and the
/// source text is kept next to it. What the editor does not show (source, notes' siblings, attributes, extra
/// elements, the source file) goes in <see cref="TranslationItem.FormatData"/> under the <see cref="XliffFormat"/>
/// keys so <c>XliffSaveStrategy</c> can write the unit back unchanged.
/// </summary>
public class XliffLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.Xliff;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.xlf", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.xliff", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            try { items.AddRange(ParseXliff(file, Path.GetRelativePath(folder, file))); }
            catch { }
        }
        return items;
    }

    private static List<TranslationItem> ParseXliff(string filePath, string relative)
    {
        var result = new List<TranslationItem>();
        var doc = XDocument.Load(filePath);
        if (doc.Root == null) return result;

        var ns = doc.Root.GetDefaultNamespace();
        var version = doc.Root.Attribute("version")?.Value ?? "1.2";

        // XLIFF 1.2: <file source-language="xx" target-language="xx"><body><trans-unit id="key"><source/><target/>
        foreach (var fileEl in doc.Root.Elements(ns + "file"))
        {
            var body = fileEl.Element(ns + "body") ?? fileEl.Element(ns + "group");
            if (body == null) continue;

            var explicitTarget = fileEl.Attribute("target-language")?.Value ?? fileEl.Attribute("target")?.Value;
            var targetLang = explicitTarget ?? "unknown";
            var sourceLang = fileEl.Attribute("source-language")?.Value;
            var fileAttrs = Attrs(fileEl, "source-language", "target-language");

            foreach (var tu in body.Descendants(ns + "trans-unit"))
            {
                var id = tu.Attribute("id")?.Value ?? "";
                if (string.IsNullOrEmpty(id)) continue;

                var source = tu.Element(ns + "source")?.Value ?? "";
                var targetEl = tu.Element(ns + "target");
                // A source-only file (no target language) has nothing to translate into yet: show the source.
                var value = targetEl?.Value ?? (explicitTarget == null ? source : "");

                var data = NewData(relative, version, sourceLang, source);
                Put(data, XliffFormat.FileAttrs, fileAttrs);
                Put(data, XliffFormat.UnitAttrs, Attrs(tu, "id"));
                if (targetEl != null) Put(data, XliffFormat.TargetAttrs, Attrs(targetEl));
                Put(data, XliffFormat.Extra, Extra(tu, ns + "source", ns + "target", ns + "note"));

                result.Add(new TranslationItem
                {
                    Language = targetLang, Namespace = id, Value = value,
                    Comment = string.Join("\n", tu.Elements(ns + "note").Select(n => n.Value)),
                    FormatData = data,
                });
            }
        }

        // XLIFF 2.0: <xliff srcLang trgLang><file><unit id="key"><segment><source/><target/>
        foreach (var unit in doc.Root.Descendants(ns + "unit"))
        {
            var id = unit.Attribute("id")?.Value ?? "";
            if (string.IsNullOrEmpty(id)) continue;

            var segment = unit.Element(ns + "segment");
            var explicitTarget = doc.Root.Attribute("trgLang")?.Value;
            var source = segment?.Element(ns + "source")?.Value ?? "";
            var targetEl = segment?.Element(ns + "target");
            var value = targetEl?.Value ?? (explicitTarget == null ? source : "");

            var data = NewData(relative, version, doc.Root.Attribute("srcLang")?.Value, source);
            Put(data, XliffFormat.UnitAttrs, Attrs(unit, "id"));
            if (segment != null) Put(data, XliffFormat.SegmentAttrs, Attrs(segment));
            if (targetEl != null) Put(data, XliffFormat.TargetAttrs, Attrs(targetEl));
            Put(data, XliffFormat.Extra, Extra(unit, ns + "segment", ns + "notes"));

            result.Add(new TranslationItem
            {
                Language = explicitTarget ?? "unknown", Namespace = id, Value = value,
                Comment = string.Join("\n", unit.Elements(ns + "notes").Elements(ns + "note").Select(n => n.Value)),
                FormatData = data,
            });
        }

        return result;
    }

    private static Dictionary<string, string> NewData(string relative, string version, string? sourceLang, string source)
    {
        var data = new Dictionary<string, string>
        {
            [XliffFormat.File] = relative,
            [XliffFormat.Version] = version,
            [XliffFormat.Source] = source,
        };
        if (!string.IsNullOrEmpty(sourceLang)) data[XliffFormat.SourceLang] = sourceLang;
        return data;
    }

    private static void Put(Dictionary<string, string> data, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) data[key] = value;
    }

    /// <summary>The element's attributes except the named ones, as JSON keyed by expanded name; null when none.</summary>
    private static string? Attrs(XElement element, params string[] except)
    {
        var attrs = element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && !except.Contains(a.Name.LocalName))
            .ToDictionary(a => a.Name.ToString(), a => a.Value);
        return attrs.Count == 0 ? null : JsonSerializer.Serialize(attrs);
    }

    /// <summary>The raw XML of the element's children other than the named ones; null when none.</summary>
    private static string? Extra(XElement element, params XName[] except)
    {
        var others = element.Elements().Where(e => !except.Contains(e.Name)).ToList();
        return others.Count == 0 ? null : string.Concat(others.Select(e => e.ToString(SaveOptions.DisableFormatting)));
    }
}

/// <summary>Keys used in <see cref="TranslationItem.FormatData"/> for XLIFF units.</summary>
public static class XliffFormat
{
    public const string File = "xliff.file";
    public const string Version = "xliff.version";
    public const string Source = "xliff.source";
    public const string SourceLang = "xliff.source_lang";
    public const string FileAttrs = "xliff.file_attrs";
    public const string UnitAttrs = "xliff.unit_attrs";
    public const string SegmentAttrs = "xliff.segment_attrs";
    public const string TargetAttrs = "xliff.target_attrs";
    public const string Extra = "xliff.extra";
}
