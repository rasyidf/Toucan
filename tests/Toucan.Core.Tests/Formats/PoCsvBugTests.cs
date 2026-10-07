using Toucan.Core.Services.LoadStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>Regression tests for FMT-01 (PO language) and FMT-04 (CSV multi-line values).</summary>
public sealed class PoCsvBugTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-pocsv-" + Guid.NewGuid().ToString("N"));

    public PoCsvBugTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Po(string? lang, string id, string str) =>
        (lang is null ? "" : $"msgid \"\"\nmsgstr \"\"\n\"Language: {lang}\\n\"\n\n") + $"msgid \"{id}\"\nmsgstr \"{str}\"\n";

    [Fact]
    public void Po_GettextLayout_ResolvesLanguageFromHeader()
    {
        Write("fr/LC_MESSAGES/messages.po", Po("fr", "Hello", "Bonjour"));
        Write("de/LC_MESSAGES/messages.po", Po("de", "Hello", "Hallo"));

        var items = new PoLoadStrategy().Load(_folder).ToList();

        Assert.Equal(["de", "fr"], items.Select(i => i.Language).Order());
        Assert.Equal("Bonjour", items.Single(i => i.Language == "fr").Value);
    }

    [Fact]
    public void Po_NoHeader_UsesLcMessagesFolder()
    {
        Write("pt_BR/LC_MESSAGES/messages.po", Po(null, "Hello", "Ola"));

        Assert.Equal("pt_BR", new PoLoadStrategy().Load(_folder).Single().Language);
    }

    [Fact]
    public void Po_FlatFile_UsesFileName()
    {
        Write("fr.po", Po(null, "Hello", "Bonjour"));

        Assert.Equal("fr", new PoLoadStrategy().Load(_folder).Single().Language);
    }

    [Fact]
    public void Po_HeaderWinsOverFileName()
    {
        Write("messages.po", Po("es", "Hello", "Hola"));

        Assert.Equal("es", new PoLoadStrategy().Load(_folder).Single().Language);
    }

    [Fact]
    public void Csv_QuotedLineBreak_IsKept()
    {
        Write("t.csv", "key,en\nk,\"line1\nline2\"\nj,plain\n");

        var items = new CsvLoadStrategy().Load(_folder).ToList();

        Assert.Equal("line1\nline2", items.Single(i => i.Namespace == "k").Value);
        Assert.Equal("plain", items.Single(i => i.Namespace == "j").Value);
    }

    [Fact]
    public void Csv_CrLfRecordsAndEscapedQuotes_Parse()
    {
        Write("t.csv", "key,en\r\na,\"say \"\"hi\"\", ok\"\r\nb,x\r\n");

        var items = new CsvLoadStrategy().Load(_folder).ToList();

        Assert.Equal("say \"hi\", ok", items.Single(i => i.Namespace == "a").Value);
        Assert.Equal(2, items.Count);
    }
}
