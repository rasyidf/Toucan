using System.Xml;
using System.Xml.Linq;

namespace Toucan.Core.Services.SaveStrategies;

internal static class XmlFile
{
    /// <summary>
    /// Saves like <c>XDocument.Save(path)</c> but writes carriage returns inside text as <c>&amp;#xD;</c>;
    /// the default writer turns them into line feeds, so CRLF in a translation would not survive a reload.
    /// </summary>
    /// The file keeps its line endings (<see cref="LineEndings"/>); line breaks inside text are written the same way, and
    /// XML readers turn them back into line feeds.
    public static void Save(XDocument doc, string path)
    {
        var settings = new XmlWriterSettings { Indent = true, NewLineHandling = NewLineHandling.Entitize, Encoding = new System.Text.UTF8Encoding(true) };
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings)) doc.Save(writer);
        var text = new System.Text.UTF8Encoding(false).GetString(buffer.ToArray()).TrimStart('\uFEFF');
        AtomicFile.WriteAllText(path, LineEndings.ForFile(path, text), settings.Encoding);
    }
}
