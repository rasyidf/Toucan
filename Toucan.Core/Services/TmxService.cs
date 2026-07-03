using System.Xml.Linq;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// TMX 1.4 import/export. Static utility — no state, no DI needed.
/// ponytail: minimal TMX subset (no props, no notes). Upgrade path: full TMX 1.4b with metadata.
/// </summary>
public static class TmxService
{
    private static readonly XNamespace s_xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>Export TM entries to a standard TMX 1.4 file.</summary>
    public static void ExportTmx(IEnumerable<TmEntry> entries, string filePath)
    {
        var body = new XElement("body");
        foreach (var entry in entries)
        {
            var tu = new XElement("tu",
                new XElement("tuv",
                    new XAttribute(s_xml + "lang", entry.SourceLang),
                    new XElement("seg", entry.SourceText)),
                new XElement("tuv",
                    new XAttribute(s_xml + "lang", entry.TargetLang),
                    new XElement("seg", entry.TargetText)));

            if (entry.CreatedDate.HasValue)
                tu.SetAttributeValue("creationdate", entry.CreatedDate.Value.ToString("yyyyMMdd'T'HHmmss'Z'"));

            body.Add(tu);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("tmx",
                new XAttribute("version", "1.4"),
                new XElement("header",
                    new XAttribute("creationtool", "Toucan"),
                    new XAttribute("creationtoolversion", "1.0"),
                    new XAttribute("datatype", "plaintext"),
                    new XAttribute("segtype", "sentence"),
                    new XAttribute("adminlang", "en-US"),
                    new XAttribute("srclang", "*all*")),
                body));

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        doc.Save(filePath);
    }

    /// <summary>Import TM entries from a TMX file.</summary>
    public static List<TmEntry> ImportTmx(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var results = new List<TmEntry>();

        var body = doc.Root?.Element("body");
        if (body == null) return results;

        foreach (var tu in body.Elements("tu"))
        {
            var tuvs = tu.Elements("tuv").ToList();
            if (tuvs.Count < 2) continue;

            // Parse creationdate if present
            DateTime? created = null;
            var dateAttr = tu.Attribute("creationdate");
            if (dateAttr != null && DateTime.TryParseExact(dateAttr.Value, "yyyyMMdd'T'HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                created = dt.ToUniversalTime();
            }

            var sourceTuv = tuvs[0];
            var sourceLang = sourceTuv.Attribute(s_xml + "lang")?.Value ?? "en";
            var sourceText = sourceTuv.Element("seg")?.Value ?? "";

            // Each subsequent tuv is a target
            for (int i = 1; i < tuvs.Count; i++)
            {
                var targetTuv = tuvs[i];
                var targetLang = targetTuv.Attribute(s_xml + "lang")?.Value ?? "";
                var targetText = targetTuv.Element("seg")?.Value ?? "";

                if (!string.IsNullOrWhiteSpace(sourceText) && !string.IsNullOrWhiteSpace(targetText))
                    results.Add(new TmEntry(sourceLang, sourceText, targetLang, targetText, created));
            }
        }

        return results;
    }
}
