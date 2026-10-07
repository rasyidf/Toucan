using System.Xml;
using System.Xml.Linq;

namespace Toucan.Core.Services.SaveStrategies;

internal static class XmlFile
{
    /// <summary>
    /// Saves like <c>XDocument.Save(path)</c> but writes carriage returns inside text as <c>&amp;#xD;</c>;
    /// the default writer turns them into line feeds, so CRLF in a translation would not survive a reload.
    /// </summary>
    public static void Save(XDocument doc, string path)
    {
        var settings = new XmlWriterSettings { Indent = true, NewLineHandling = NewLineHandling.Entitize };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }
}
