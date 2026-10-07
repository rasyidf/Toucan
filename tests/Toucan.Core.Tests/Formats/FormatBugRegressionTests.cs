using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>Regression tests for FMT-05 (RESX language), FMT-06 (XLIFF), FMT-07 and FMT-08 (ARB). Inputs are the repros from docs/known-bugs.md.</summary>
public sealed class FormatBugRegressionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-fmt-" + Guid.NewGuid().ToString("N"));
    private readonly FileService _files = new(NullLogger<FileService>.Instance);

    public FormatBugRegressionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static SaveContext Context(List<TranslationItem> items) => new()
    {
        Languages = items.Select(i => i.Language).Distinct().ToList(),
        LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
        NsTreeItems = [],
    };

    // ---- FMT-05

    private const string ResxBody = "<root><data name=\"a\"><value>x</value></data></root>";

    [Theory]
    [InlineData("Resources.resx", "default")]
    [InlineData("Resources.fr.resx", "fr")]
    [InlineData("Views.Home.Index.resx", "default")]
    [InlineData("Resources.Designer.resx", "default")]
    [InlineData("Resources.zh-Hans.resx", "zh-Hans")]
    [InlineData("Resources.pt-BR.resx", "pt-BR")]
    public void Resx_OnlyCultureNamesAreLanguages(string file, string expected)
    {
        Write(file, ResxBody);

        var item = Assert.Single(new ResxLoadStrategy().Load(_folder));

        Assert.Equal(expected, item.Language);
    }

    // ---- FMT-06

    private const string Angular = """
        <?xml version="1.0" encoding="utf-8"?>
        <xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2">
          <file source-language="en" target-language="fr" datatype="plaintext" original="ng2.template">
            <body>
              <trans-unit id="a1b2c3" datatype="html">
                <source>Welcome</source>
                <target state="translated">Bienvenue</target>
                <context-group purpose="location"><context context-type="sourcefile">app.html</context><context context-type="linenumber">3</context></context-group>
                <note priority="1" from="description">Greeting</note>
              </trans-unit>
              <trans-unit id="d4e5f6"><source>Bye</source></trans-unit>
            </body>
          </file>
        </xliff>
        """;

    [Fact]
    public void Xliff_AngularFileRoundTripsWithSourceNoteAndState()
    {
        var path = Write("messages.fr.xlf", Angular);
        var items = new XliffLoadStrategy().Load(_folder).ToList();

        new XliffSaveStrategy(_files).Save(_folder, Context(items));

        Assert.False(File.Exists(Path.Combine(_folder, "fr.xlf")));
        var saved = File.ReadAllText(path);
        Assert.Contains("<source>Welcome</source>", saved);
        Assert.Contains("Bienvenue", saved);
        Assert.Contains("state=\"translated\"", saved);
        Assert.Contains("Greeting", saved);
        Assert.Contains("datatype=\"html\"", saved);
        Assert.Contains("original=\"ng2.template\"", saved);
        Assert.Contains("source-language=\"en\"", saved);
        Assert.Contains("target-language=\"fr\"", saved);
        Assert.Contains("sourcefile", saved);
        Assert.Contains("<source>Bye</source>", saved);
    }

    [Fact]
    public void Xliff_Load_ShowsUntranslatedUnitAsEmpty()
    {
        Write("messages.fr.xlf", Angular);

        var items = new XliffLoadStrategy().Load(_folder).ToList();

        Assert.Equal("Bienvenue", items.Single(i => i.Namespace == "a1b2c3").Value);
        Assert.Equal("", items.Single(i => i.Namespace == "d4e5f6").Value);
        Assert.Equal("Greeting", items.Single(i => i.Namespace == "a1b2c3").Comment);
    }

    [Fact]
    public void Xliff_Save_PromotesStateOfNewlyTranslatedUnit()
    {
        var path = Write("messages.fr.xlf", Angular.Replace("state=\"translated\"", "state=\"new\""));
        var items = new XliffLoadStrategy().Load(_folder).ToList();
        items.Single(i => i.Namespace == "d4e5f6").Value = "Au revoir";

        new XliffSaveStrategy(_files).Save(_folder, Context(items));

        var saved = File.ReadAllText(path);
        Assert.Contains("Au revoir", saved);
        Assert.DoesNotContain("state=\"new\"", saved);
    }

    [Fact]
    public void Xliff_20_KeepsVersionSourceAndNotes()
    {
        var path = Write("de.xlf", """
            <xliff version="2.0" xmlns="urn:oasis:names:tc:xliff:document:2.0" srcLang="en" trgLang="de">
              <file id="f1"><unit id="u1"><notes><note>Hint</note></notes><segment state="translated"><source>Hello</source><target>Hallo</target></segment></unit></file>
            </xliff>
            """);
        var items = new XliffLoadStrategy().Load(_folder).ToList();

        new XliffSaveStrategy(_files).Save(_folder, Context(items));

        var saved = File.ReadAllText(path);
        Assert.Contains("version=\"2.0\"", saved);
        Assert.Contains("srcLang=\"en\"", saved);
        Assert.Contains("<source>Hello</source>", saved);
        Assert.Contains("Hint", saved);
        Assert.Contains("state=\"translated\"", saved);
    }

    // ---- FMT-07

    [Fact]
    public void Arb_KeepsKeyMetadataAndOtherHeaderEntries()
    {
        var path = Write("app_en.arb", """{"@@locale":"en","@@last_modified":"2026-01-01","hi":"Hi {n}","@hi":{"description":"greeting","placeholders":{"n":{}}}}""");
        var items = new ArbLoadStrategy().Load(_folder).ToList();

        new ArbSaveStrategy(_files).Save(_folder, Context(items));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("en", root.GetProperty("@@locale").GetString());
        Assert.Equal("2026-01-01", root.GetProperty("@@last_modified").GetString());
        Assert.Equal("Hi {n}", root.GetProperty("hi").GetString());
        Assert.Equal("greeting", root.GetProperty("@hi").GetProperty("description").GetString());
        Assert.True(root.GetProperty("@hi").GetProperty("placeholders").TryGetProperty("n", out _));
    }

    [Fact]
    public void Arb_Save_PutsMetadataRightAfterItsKeyInStableOrder()
    {
        var path = Write("app_en.arb", """{"@@locale":"en","b":"B","a":"A","@a":{"description":"d"}}""");
        var items = new ArbLoadStrategy().Load(_folder).ToList();

        new ArbSaveStrategy(_files).Save(_folder, Context(items));
        var first = File.ReadAllText(path);
        new ArbSaveStrategy(_files).Save(_folder, Context(new ArbLoadStrategy().Load(_folder).ToList()));

        Assert.Equal(first, File.ReadAllText(path));
        var names = System.Text.Json.JsonDocument.Parse(first).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["@@locale", "a", "@a", "b"], names);
    }

    // ---- FMT-08

    [Theory]
    [InlineData("app_en.arb", "en")]
    [InlineData("app_en_US.arb", "en_US")]
    [InlineData("intl_zh_Hans_CN.arb", "zh_Hans_CN")]
    [InlineData("my_app_pt_BR.arb", "pt_BR")]
    public void Arb_LocaleFromFileName(string file, string expected)
    {
        Write(file, """{"hi":"Hi"}""");

        var item = Assert.Single(new ArbLoadStrategy().Load(_folder));

        Assert.Equal(expected, item.Language);
    }

    [Fact]
    public void Arb_LocaleHeaderWinsOverFileName()
    {
        Write("app_en_US.arb", """{"@@locale":"en_GB","hi":"Hi"}""");

        Assert.Equal("en_GB", Assert.Single(new ArbLoadStrategy().Load(_folder)).Language);
    }

    [Fact]
    public void Arb_RegionLocaleSavesBackToSameFile()
    {
        var path = Write("app_en_US.arb", """{"hi":"Hi"}""");
        var items = new ArbLoadStrategy().Load(_folder).ToList();

        new ArbSaveStrategy(_files).Save(_folder, Context(items));

        Assert.Single(Directory.GetFiles(_folder));
        Assert.Contains("\"hi\"", File.ReadAllText(path));
    }
}
