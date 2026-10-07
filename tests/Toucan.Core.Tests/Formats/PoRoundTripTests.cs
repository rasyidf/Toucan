using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Extensions;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>Regression tests for FMT-01 (save side), FMT-02 and FMT-03.</summary>
public sealed class PoRoundTripTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-po-" + Guid.NewGuid().ToString("N"));

    public PoRoundTripTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private const string Realistic = """
        # Translator note
        msgid ""
        msgstr ""
        "Language: de\n"
        "Plural-Forms: nplurals=2; plural=(n != 1);\n"

        #: src/a.py:3
        #, fuzzy
        msgid "Hello"
        msgstr "Hallo"

        msgctxt "menu"
        msgid "Open"
        msgstr "Oeffnen"

        msgid "one apple"
        msgid_plural "%d apples"
        msgstr[0] "ein Apfel"
        msgstr[1] "%d Aepfel"

        msgid "multi"
        msgstr ""
        "line1\n"
        "line2"

        """;

    private List<TranslationItem> Load() => new PoLoadStrategy().Load(_folder).ToList();

    private void Save(List<TranslationItem> items)
    {
        var ctx = new SaveContext
        {
            Languages = items.Select(i => i.Language).Distinct().ToList(),
            LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = items.ToNsTree().ToList(),
        };
        new PoSaveStrategy(new FileService(NullLogger<FileService>.Instance)).Save(_folder, ctx);
    }

    [Fact]
    public void Load_KeepsContextPluralsAndFlags()
    {
        File.WriteAllText(Path.Combine(_folder, "de.po"), Realistic);

        var items = Load();

        Assert.Equal(4, items.Count);
        Assert.Contains(items, i => i.Namespace == "menu\u0004Open" && i.Value == "Oeffnen");
        var plural = items.Single(i => i.Namespace == "one apple");
        Assert.Equal("ein Apfel", plural.Value);
        Assert.Equal("%d Aepfel", plural.FormatData![PoFormat.PluralPrefix + "1"]);
        Assert.False(items.Single(i => i.Namespace == "Hello").IsApproved);
        Assert.Equal("line1\nline2", items.Single(i => i.Namespace == "multi").Value);
    }

    [Fact]
    public void Save_UnchangedFile_RoundTripsByteForByte()
    {
        var path = Path.Combine(_folder, "de.po");
        File.WriteAllText(path, Realistic);

        Save(Load());

        Assert.Equal((Realistic + "\n").Replace("\r\n", "\n"), File.ReadAllText(path).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Save_GettextLayout_WritesBackToOriginalFile()
    {
        var dir = Path.Combine(_folder, "fr", "LC_MESSAGES");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "messages.po"), "msgid \"\"\nmsgstr \"\"\n\"Language: fr\\n\"\n\nmsgid \"Hello\"\nmsgstr \"Bonjour\"\n");

        var items = Load();
        items[0].Value = "Salut";
        Save(items);

        Assert.Contains("msgstr \"Salut\"", File.ReadAllText(Path.Combine(dir, "messages.po")));
        Assert.False(File.Exists(Path.Combine(_folder, "fr.po")));
    }

    [Fact]
    public void Save_NewEntry_HasNoContextOrFuzzy()
    {
        File.WriteAllText(Path.Combine(_folder, "de.po"), Realistic);
        var items = Load();
        items.Add(new TranslationItem { Language = "de", Namespace = "Save", Value = "Speichern" });

        Save(items);

        var text = File.ReadAllText(Path.Combine(_folder, "de.po"));
        Assert.Contains("msgid \"Save\"\nmsgstr \"Speichern\"".Replace("\n", Environment.NewLine), text);
        Assert.DoesNotContain("msgctxt \"Save\"", text);
    }
}
