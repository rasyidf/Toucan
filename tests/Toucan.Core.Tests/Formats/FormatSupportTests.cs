using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>The format support matrix: every built-in format declares it, docs/formats.md is generated from it, and unsafe saves are blocked.</summary>
public sealed class FormatSupportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-fs-" + Guid.NewGuid().ToString("N"));

    public FormatSupportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ProjectService Service() => new(
        new FileService(NullLogger<FileService>.Instance),
        FormatTestHost.SaveStrategies,
        FormatTestHost.Factory,
        new ProjectModeResolver(),
        NullLogger<ProjectService>.Instance);

    private static IEnumerable<ISaveStrategy> BuiltIns => FormatTestHost.SaveStrategies.Where(s => s.Support != null);

    [Fact]
    public void EveryBuiltInFormatDeclaresSupport()
    {
        foreach (var s in BuiltIns)
        {
            var support = Assert.IsType<FormatSupport>(s.Support, exactMatch: false);
            Assert.False(string.IsNullOrWhiteSpace(support.Versions), s.FormatId);
            Assert.NotEmpty(support.Preserved);
        }
    }

    [Fact]
    public void FullyEditableFormatsCanBeLoadedAgain()
    {
        foreach (var s in BuiltIns.Where(s => s.Support!.Editing == FormatEditing.Full))
            Assert.NotNull(FormatTestHost.Factory.GetLoadStrategy(s.FormatId));
    }

    [Fact]
    public void MatrixDocumentIsUpToDate()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Toucan.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "formats.md");
        var expected = BuildMatrix();

        if (Environment.GetEnvironmentVariable("TOUCAN_UPDATE_DOCS") == "1") File.WriteAllText(path, expected);

        Assert.True(File.Exists(path), "docs/formats.md is missing. Run the tests with TOUCAN_UPDATE_DOCS=1 to generate it.");
        Assert.Equal(expected.ReplaceLineEndings("\n"), File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    private static string BuildMatrix()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: \"Format support matrix\"");
        sb.AppendLine("status: active");
        sb.AppendLine("updated: 2026-11-26");
        sb.AppendLine("summary: \"Which file formats Toucan edits safely, what survives a save, and what does not.\"");
        sb.AppendLine("---");
        sb.AppendLine("# Format support matrix");
        sb.AppendLine();
        sb.AppendLine("Generated from the format strategies by `FormatSupportTests`; to refresh it run the Core tests with `TOUCAN_UPDATE_DOCS=1`. Do not edit by hand.");
        sb.AppendLine();
        sb.AppendLine("**Full**: everything listed under *Kept* survives load and save. **Limited**: strings are editable, but the items under *Not supported* are dropped or rewritten on save. ");
        sb.AppendLine("Where a format can lose content that is already in the project's files (Android XML and RESX), Toucan checks the files when the project opens and again before saving: it warns, and refuses to overwrite them. Use Save As to write a copy.");
        sb.AppendLine();
        sb.AppendLine("**Saving** says what a save does to the files. **Edits in place**: only the entries you edited change; comments, order and formatting stay. This applies when Toucan opens the folder through the format's framework (ARB, Java ResourceBundle, i18next) or maps its files exactly (JSON); Project Properties shows which way the open project saves. **Rewrites the file**: the file is written again from the project, keeping what is listed under *Kept*; comments and formatting in the file may change. **Export only** and **Read only** formats are written but not read back, or read but not written.");
        sb.AppendLine();
        sb.AppendLine("**Line endings.** Every save keeps each file's line endings (LF or CRLF), so a project moved between Windows, macOS and Linux does not change whole. A new file, such as a new language, takes the line endings of the project's files next to it; with none to copy, it uses the operating system's (CRLF on Windows, LF elsewhere). Toucan's own files in the project (`toucan.tproj`, `.toucan/`, comment and audit sidecars) follow the same rule. **Line ending** in Project Properties overrides this for the translation files. In CSV, only the rows follow the file's line endings: a line break inside a quoted value is written as the value holds it, as spreadsheets do.");
        sb.AppendLine();
        sb.AppendLine("| Format | ID | Editing | Saving | Versions | Kept | Not supported |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var s in BuiltIns.OrderBy(s => s.DisplayName, StringComparer.Ordinal))
        {
            var f = s.Support!;
            static string Escape(string x) => x.Replace("|", "\\|").Replace("<", "&lt;").Replace(">", "&gt;").Replace("*", "\\*");
            string Cell(IReadOnlyList<string> xs) => string.Join("<br>", xs.Select(Escape));
            var saving = Toucan.Core.Services.FormatSaving.Label(Toucan.Core.Services.FormatSaving.Best(s, FormatTestHost.Factory.GetLoadStrategy(s.FormatId)));
            sb.AppendLine($"| {s.DisplayName} | `{s.FormatId}` | {f.Editing} | {saving} | {Escape(f.Versions)} | {Cell(f.Preserved)} | {Cell(f.Unsupported)} |");
        }
        return sb.ToString();
    }

    // ---- unsafe saves

    private const string AndroidWithPlurals = """
        <resources>
          <string name="app">App</string>
          <plurals name="items"><item quantity="one">%d item</item><item quantity="other">%d items</item></plurals>
        </resources>
        """;

    private void WriteAndroid(string content)
    {
        var dir = Path.Combine(_folder, "res", "values");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "strings.xml"), content);
        new ProjectSettings { ProjectPath = _folder, Languages = ["default"], SaveFormat = FormatIds.AndroidXml }.Save();
    }

    [Fact]
    public void AndroidPlurals_WarnOnOpenAndBlockSave()
    {
        WriteAndroid(AndroidWithPlurals);
        var service = Service();

        var loaded = service.LoadProject(_folder);

        Assert.Contains(loaded.Warnings, w => w.Contains("<plurals>"));
        var ex = Assert.Throws<FormatSaveBlockedException>(() => service.Save(_folder, FormatIds.AndroidXml, [], loaded.Translations));
        Assert.Contains("<plurals>", ex.Message);
        Assert.Contains("<plurals", File.ReadAllText(Path.Combine(_folder, "res", "values", "strings.xml")));
    }

    [Fact]
    public void AndroidWithOnlyStrings_SavesNormally()
    {
        WriteAndroid("<resources><string name=\"app\">App</string></resources>");
        var service = Service();

        var loaded = service.LoadProject(_folder);

        Assert.Empty(loaded.Warnings);
        service.Save(_folder, FormatIds.AndroidXml, [], loaded.Translations);
    }

    [Fact]
    public void AndroidPlurals_SaveAsToNewFolderIsAllowed()
    {
        WriteAndroid(AndroidWithPlurals);
        var service = Service();
        var loaded = service.LoadProject(_folder);
        var copy = Path.Combine(_folder, "copy");
        Directory.CreateDirectory(copy);

        service.Save(copy, FormatIds.AndroidXml, [], loaded.Translations);

        Assert.True(File.Exists(Path.Combine(copy, "res", "values", "strings.xml")));
    }

    [Theory]
    [InlineData("<root><data name=\"icon\" type=\"System.Drawing.Bitmap\"><value>AAAA</value></data></root>", "non-string resources")]
    [InlineData("<root><metadata name=\"x\"><value>1</value></metadata></root>", "<metadata>")]
    public void ResxWithUnsupportedContent_BlocksSave(string content, string expected)
    {
        File.WriteAllText(Path.Combine(_folder, "Resources.resx"), content);
        new ProjectSettings { ProjectPath = _folder, Languages = ["default"], SaveFormat = FormatIds.Resx }.Save();
        var service = Service();
        var loaded = service.LoadProject(_folder);

        Assert.Contains(loaded.Warnings, w => w.Contains(expected));
        Assert.Throws<FormatSaveBlockedException>(() => service.Save(_folder, FormatIds.Resx, [], loaded.Translations));
    }

    [Fact]
    public void ResxNotNamedResources_BlocksSave()
    {
        File.WriteAllText(Path.Combine(_folder, "Strings.resx"), "<root><data name=\"a\"><value>x</value></data></root>");
        new ProjectSettings { ProjectPath = _folder, Languages = ["default"], SaveFormat = FormatIds.Resx }.Save();
        var service = Service();

        var loaded = service.LoadProject(_folder);

        Assert.Contains(loaded.Warnings, w => w.Contains("Strings.resx"));
        Assert.Throws<FormatSaveBlockedException>(() => service.Save(_folder, FormatIds.Resx, [], loaded.Translations));
    }

    // ---- JSON scalar types

    [Fact]
    public void JsonNumbersAndBooleans_StayTypedUntilEdited()
    {
        File.WriteAllText(Path.Combine(_folder, "en.json"), """{ "limit": 10, "on": true, "name": "x" }""");
        var service = Service();
        new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveFormat = FormatIds.Json }.Save();
        var loaded = service.LoadProject(_folder);

        loaded.Translations.Single(t => t.Namespace == "on").Value = "false";
        service.Save(_folder, FormatIds.Json, [], loaded.Translations);

        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "en.json"))).RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Number, json.GetProperty("limit").ValueKind);
        Assert.Equal(10, json.GetProperty("limit").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.String, json.GetProperty("on").ValueKind);
        Assert.Equal("false", json.GetProperty("on").GetString());
    }

    [Fact]
    public void JsonUntouchedBoolean_IsWrittenAsBoolean()
    {
        File.WriteAllText(Path.Combine(_folder, "en.json"), """{ "on": true }""");
        var service = Service();
        new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveFormat = FormatIds.Json }.Save();
        var loaded = service.LoadProject(_folder);

        service.Save(_folder, FormatIds.Json, [], loaded.Translations);

        Assert.Equal(System.Text.Json.JsonValueKind.True,
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "en.json"))).RootElement.GetProperty("on").ValueKind);
    }
}
