using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// A project moved between Windows and macOS or Linux must not change whole on its next save: every file keeps its line
/// endings, and a new file follows the files next to it (<see cref="LineEndings"/>).
/// </summary>
public sealed class FormatLineEndingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-eol-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    public static TheoryData<string, string> FormatsAndEndings()
    {
        var data = new TheoryData<string, string>();
        foreach (var s in FormatTestHost.EditableStrategies.Where(s => FormatTestHost.Factory.GetLoadStrategy(s.FormatId) != null))
            foreach (var eol in new[] { "LF", "CRLF" })
                data.Add(s.FormatId, eol);
        return data;
    }

    private static string Newline(string name) => name == "CRLF" ? "\r\n" : "\n";

    /// <summary>
    /// CSV writes a line break inside a quoted value as the value holds it, as spreadsheets do (a cell's line break is not
    /// the file's line ending), so its sample has no multiline value; <c>FormatFidelityFixtureTests</c> covers those.
    /// </summary>
    private static List<TranslationItem> Sample(string formatId, params string[] languages) =>
    [
        .. languages.SelectMany(l => new[]
        {
            new TranslationItem { Language = l, Namespace = "app.title", Value = $"Title {l}" },
            new TranslationItem { Language = l, Namespace = "app.multi", Value = formatId == FormatIds.Csv ? $"One line {l}" : $"Line one {l}\nLine two" },
            new TranslationItem { Language = l, Namespace = "buttons.save", Value = $"Save {l}" },
        }),
    ];

    private static SaveContext Context(List<TranslationItem> items) => new()
    {
        LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
        NsTreeItems = items.ToNsTree().ToList(),
        Languages = [.. items.Select(i => i.Language).Distinct()],
    };

    /// <summary>Writes every file of <paramref name="folder"/> again with <paramref name="newline"/>, as a checkout on another OS would.</summary>
    private static void Convert(string folder, string newline)
    {
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = System.Text.Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            File.WriteAllText(file, text.ReplaceLineEndings(newline), new System.Text.UTF8Encoding(bom));
        }
    }

    private static void AssertEveryFileUses(string folder, string newline)
    {
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var crlf = text.Split("\r\n").Length - 1;
            var lf = text.Count(c => c == '\n') - crlf;
            var name = Path.GetRelativePath(folder, file);
            if (newline == "\r\n") Assert.True(lf == 0, $"{name} has {lf} LF line(s) in a CRLF project.");
            else Assert.True(crlf == 0, $"{name} has {crlf} CRLF line(s) in an LF project.");
        }
    }

    [Theory]
    [MemberData(nameof(FormatsAndEndings))]
    public void SavingKeepsTheFilesLineEndings(string formatId, string ending)
    {
        var newline = Newline(ending);
        var strategy = FormatTestHost.Factory.GetSaveStrategy(formatId)!;
        strategy.Save(_root, Context(Sample(formatId, "en", "fr")));
        Convert(_root, newline);

        var loaded = FormatTestHost.Factory.GetLoadStrategy(formatId)!.Load(_root).ToList();
        // The file's line endings are not part of a value: a multiline value reads with line feeds either way.
        if (formatId != FormatIds.Csv)
            Assert.All(loaded.Where(t => t.Namespace.EndsWith("multi", StringComparison.Ordinal)), t => Assert.Equal($"Line one {t.Language}\nLine two", t.Value));
        foreach (var title in loaded.Where(t => t.Namespace.EndsWith("title", StringComparison.Ordinal))) title.Value += " edited";
        strategy.Save(_root, Context(loaded));

        AssertEveryFileUses(_root, newline);
    }

    [Theory]
    [MemberData(nameof(FormatsAndEndings))]
    public void ANewLanguageFollowsTheExistingFiles(string formatId, string ending)
    {
        var newline = Newline(ending);
        var strategy = FormatTestHost.Factory.GetSaveStrategy(formatId)!;
        strategy.Save(_root, Context(Sample(formatId, "en")));
        Convert(_root, newline);

        var loaded = FormatTestHost.Factory.GetLoadStrategy(formatId)!.Load(_root).ToList();
        strategy.Save(_root, Context([.. loaded, .. Sample(formatId, "fr")]));

        AssertEveryFileUses(_root, newline);
    }

    [Fact]
    public void OnlyLfAndCrLfAreConverted()
    {
        // A lone CR, U+2028 and a form feed can be characters of a value; only line breaks follow the file.
        Assert.Equal("a\r\nb\rc\u2028d\fe\r\n", LineEndings.Apply("a\nb\rc\u2028d\fe\r\n", "\r\n"));
        Assert.Equal("a\nb\rc\n", LineEndings.Apply("a\r\nb\rc\n", "\n"));
    }

    [Fact]
    public void LineEndingsAreDetectedFromMostLines()
    {
        Assert.Equal("\r\n", LineEndings.Detect("a\r\nb\r\nc\n"));
        Assert.Equal("\n", LineEndings.Detect("a\nb\n"));
        Assert.Null(LineEndings.Detect("{}"));
        Assert.Null(LineEndings.Detect(null));
    }

    [Fact]
    public void ANewFileWithNothingNextToItUsesTheOperatingSystemNewline()
    {
        var path = Path.Combine(_root, "alone", "new.json");
        Assert.Equal(Environment.NewLine, LineEndings.For(path));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void TheProjectFileKeepsItsLineEndings(string newline)
    {
        var settings = new ProjectSettings { ProjectPath = _root, Framework = "json", SaveFormat = FormatIds.Json, PrimaryLanguage = "en", Languages = ["en"] };
        settings.Save();
        var manifest = Path.Combine(_root, "toucan.tproj");
        File.WriteAllText(manifest, File.ReadAllText(manifest).ReplaceLineEndings(newline));

        settings.Languages = ["en", "fr"];
        settings.Save();

        var text = File.ReadAllText(manifest);
        Assert.Contains("fr", text);
        Assert.Equal(newline, LineEndings.Detect(text));
        var crlf = text.Split("\r\n").Length - 1;
        Assert.Equal(0, newline == "\r\n" ? text.Count(c => c == '\n') - crlf : crlf);
    }
}
