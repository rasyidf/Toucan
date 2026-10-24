using ClosedXML.Excel;
using System.IO.Compression;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class SpreadsheetExchangeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("toucan-m4-").FullName;
    private string FilePath => Path.Combine(_folder, "exchange.xlsx");
    public void Dispose() => Directory.Delete(_folder, true);
    private void Book(string[] headers, params string[][] rows)
    {
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Translations");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        book.SaveAs(FilePath);
    }
    private static TranslationItem Item(string package, string language, string value, params ResourcePathSegment[] path) => new()
    {
        Namespace = package + "." + ResourceEntryOperations.Selector(path, true), Language = language, Value = value,
        Comment = language, IsApproved = language == "en", Identity = new ResourceEntryIdentity { Package = package, ResourceId = package + language, Path = path }
    };
    private static ResourceDescriptor Destination(string language = "en") => new() { Id = "main" + language, Package = "main", Language = language, FormatId = FormatIds.Json, RelativePath = language + ".json" };

    [Fact]
    public void NativeRoundTripPreservesPackagesTypedPathsSparseLanguagesAndPerLanguageMetadata()
    {
        var items = new[] {
            Item("main", "en", "日本語 😀\nHello", ResourcePathSegment.Named("a.b")),
            Item("main", "de", "", ResourcePathSegment.Named("a.b")),
            Item("main", "en", "Nested", ResourcePathSegment.Named("a"), ResourcePathSegment.Named("b")),
            Item("checkout", "en", "Other", ResourcePathSegment.Named("a.b")),
            Item("main", "en", "Array", ResourcePathSegment.Named("choices"), ResourcePathSegment.At(0)) };
        ExcelService.Write(FilePath, items, ExcelService.Toucan);
        var table = ExcelService.Read(FilePath);
        Assert.Equal("toucan-v2", table.Dialect);
        Assert.Equal(items.Length, table.Units.Count);
        foreach (var item in items)
        {
            var unit = Assert.Single(table.Units, u => u.Package == item.Identity!.Package && u.Language == item.Language &&
                System.Text.Json.JsonSerializer.Serialize(u.Path) == item.Identity.KeyToken);
            Assert.Equal(item.Value, unit.Value); Assert.Equal(item.Comment, unit.Comment); Assert.Equal(item.IsApproved, unit.Approved);
        }
        Assert.Empty(SpreadsheetPlanner.Plan(table, items).Changes);
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+1+2")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,2)")]
    [InlineData("'quoted")]
    public void FormulaLikeValuesAreExportedAsExactText(string value)
    {
        ExcelService.Write(FilePath, [new TranslationItem { Namespace = value, Language = "en", Value = value, Comment = value }], ExcelService.Toucan);
        using var book = new XLWorkbook(FilePath);
        Assert.False(book.Worksheet(1).Cell(2, 4).HasFormula);
        Assert.Equal(XLDataType.Text, book.Worksheet(1).Cell(2, 4).DataType);
        var unit = Assert.Single(ExcelService.Read(FilePath).Units);
        Assert.Equal(value, unit.Value); Assert.Equal(value, unit.Key); Assert.Equal(value, unit.Comment);
    }

    [Fact]
    public void CompatibilityDialectKeepsMixedApprovalsAndPublicHeaders()
    {
        var items = new[] { Item("main", "en", "Hello", ResourcePathSegment.Named("title")), Item("main", "de", "Hallo", ResourcePathSegment.Named("title")) };
        foreach (var item in items) item.Comment = "Shared";
        ExcelService.Write(FilePath, items, BabelSpreadsheetDialect.Instance);
        using var book = new XLWorkbook(FilePath);
        Assert.Equal("Translation ID", book.Worksheet(1).Cell(1, 2).GetString());
        var units = ExcelService.Read(FilePath).Units;
        Assert.True(units.Single(u => u.Language == "en").Approved); Assert.False(units.Single(u => u.Language == "de").Approved);
    }

    [Fact]
    public void CompatibilityDialectRefusesLossOfLanguageCommentsAndTypedIdentity()
    {
        Assert.Throws<FormatException>(() => ExcelService.Write(FilePath,
            [Item("main", "en", "A", ResourcePathSegment.Named("a.b")), Item("main", "en", "B", ResourcePathSegment.Named("a"), ResourcePathSegment.Named("b"))], BabelSpreadsheetDialect.Instance));
        Assert.Throws<FormatException>(() => ExcelService.Write(FilePath,
            [Item("main", "en", "A", ResourcePathSegment.Named("title")), Item("main", "de", "B", ResourcePathSegment.Named("title"))], BabelSpreadsheetDialect.Instance));
    }

    [Theory]
    [InlineData("Key", "Translation ID", "en")]
    [InlineData("Key", "en", "EN")]
    [InlineData("Key", "en", "Translator note")]
    [InlineData("Key", "en", "fr approved")]
    [InlineData("Key", "en", "en present")]
    [InlineData("Key", "en", "")]
    [InlineData("Path", "Key", "en")]
    public void AmbiguousDuplicateAndUnknownHeadersAreRejected(string a, string b, string c)
    {
        Book([a, b, c], ["title", "Hello", "bad"]);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
    }

    [Fact]
    public void ReorderedColumnsMapMetadataByLanguageAndMissingColumnsStayUnchanged()
    {
        Book(["de approved", "Translation ID", "de", "Package", "en", "en approved"], ["No", "title", "Hallo", "main", "Hello", "Yes"]);
        var current = new[] { Item("main", "en", "Hello", ResourcePathSegment.Named("title")), Item("main", "de", "Old", ResourcePathSegment.Named("title")), Item("main", "fr", "Bonjour", ResourcePathSegment.Named("title")) };
        var preview = SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), current);
        Assert.Equal(1, preview.Edits); Assert.Empty(preview.Conflicts);
        Assert.Equal("Bonjour", preview.Items.Single(i => i.Language == "fr").Value);
        Assert.Equal("de", preview.Items.Single(i => i.Language == "de").Comment);
        Assert.False(preview.Items.Single(i => i.Language == "de").IsApproved);
    }

    [Fact]
    public void NativeFalsePresenceOmitsUnitButTrueBlankClearsAndLegacyBlankStillClears()
    {
        var item = Item("main", "en", "Original", ResourcePathSegment.Named("title"));
        Book(["Package", "Key", "Path", "en", "en present"], ["main", "title", "", "", "false"]);
        Assert.Empty(SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [item]).Changes);
        Book(["Package", "Key", "Path", "en", "en present"], ["main", "title", "", "", "true"]);
        Assert.Equal(1, SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [item]).Clears);
        Book(["Key", "en"], ["title", ""]);
        Assert.Equal(1, SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [item]).Clears);
    }

    [Fact]
    public void SameKeyInDifferentPackagesDoesNotUseDisplayPrefixes()
    {
        var a = Item("main", "en", "A", ResourcePathSegment.Named("title"));
        var b = Item("checkout", "en", "B", ResourcePathSegment.Named("title"));
        a.Namespace = "DisplayA"; b.Namespace = "DisplayB";
        // Namespace mutation is itself a rename. Give identities their actual resource keys again.
        a.Identity = a.Identity! with { Path = [ResourcePathSegment.Named("title")] };
        b.Identity = b.Identity! with { Path = [ResourcePathSegment.Named("title")] };
        Book(["Package", "Key", "en"], ["checkout", "title", "Changed"]);
        var preview = SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [a, b]);
        Assert.Single(preview.Changes); Assert.Equal("A", preview.Items.Single(i => i.Identity!.Package == "main").Value);
        Assert.Equal("Changed", preview.Items.Single(i => i.Identity!.Package == "checkout").Value);
        Book(["Key", "en"], ["checkout.title", "Must not guess a package"]);
        var legacy = SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [a, b]);
        Assert.Single(legacy.Conflicts);
        Assert.Empty(legacy.Changes);
        Assert.False(legacy.CanApply);
    }

    [Theory]
    [InlineData("other", "en", "title")]
    [InlineData("main", "fr", "title")]
    public void UnknownPackageOrLanguageBlocksWithoutChangingInput(string package, string language, string key)
    {
        var item = Item("main", "en", "Original", ResourcePathSegment.Named("title"));
        Book(["Package", "Key", language], [package, key, "Changed"]);
        var preview = SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [item], [Destination()]);
        Assert.Single(preview.Conflicts); Assert.False(preview.CanApply); Assert.Equal("Original", item.Value);
    }

    [Fact]
    public void AdditionsNeedUniqueDestinationAndRejectParentChildCollision()
    {
        Book(["Key", "en"], ["new", "Added"]);
        var table = ExcelService.Read(FilePath);
        var item = Item("main", "en", "Original", ResourcePathSegment.Named("title"));
        Assert.Single(SpreadsheetPlanner.Plan(table, [item]).Conflicts);
        Assert.Single(SpreadsheetPlanner.Plan(table, [item], [Destination(), Destination() with { Id = "other" }]).Conflicts);
        var preview = SpreadsheetPlanner.Plan(table, [item], [Destination()]);
        Assert.Equal(1, preview.Additions); Assert.Empty(preview.Conflicts);
        Assert.Equal("mainen", preview.Items.Single(i => i.Value == "Added").Identity!.ResourceId);
        Book(["Key", "en"], ["title.child", "Invalid"]);
        Assert.Single(SpreadsheetPlanner.Plan(ExcelService.Read(FilePath), [item], [Destination()]).Conflicts);
    }

    [Fact]
    public void DuplicateRowsInvalidFlagsAndMalformedTypedPathsAreRejected()
    {
        Book(["Key", "en"], ["title", "A"], ["title", "B"]);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en", "Approved"], ["title", "A", "perhaps"]);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Package", "Key", "Path", "en", "en present"], ["main", "title", "[]", "A", "true"]);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en"], ["", "Orphan"]);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
    }

    [Fact]
    public void FormulasAndMultipleSheetsAreRejected()
    {
        Book(["Key", "en"], ["title", "A"]);
        using (var book = new XLWorkbook(FilePath)) { book.Worksheet(1).Cell(2, 2).FormulaA1 = "1+2"; book.Save(); }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en"], ["title", "A"]);
        using (var book = new XLWorkbook(FilePath)) { book.AddWorksheet("Other"); book.Save(); }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
    }

    [Fact]
    public void SparseCompatibilityExportAndOversizedTextAreRejectedWithoutWriting()
    {
        Assert.Throws<FormatException>(() => ExcelService.Write(FilePath,
            [Item("main", "en", "Hello", ResourcePathSegment.Named("title")), Item("other", "de", "Hallo", ResourcePathSegment.Named("title"))], BabelSpreadsheetDialect.Instance));
        Assert.False(File.Exists(FilePath));
        Assert.Throws<FormatException>(() => ExcelService.Write(FilePath,
            [new TranslationItem { Namespace = "key", Language = "en", Value = new string('a', 32_768) }], ExcelService.Toucan));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ExpandedArchiveColumnAndUnitLimitsAreEnforced()
    {
        using (var archive = ZipFile.Open(FilePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("[Content_Types].xml"); archive.CreateEntry("xl/workbook.xml");
            using var stream = archive.CreateEntry("large.xml", CompressionLevel.Fastest).Open();
            var block = new byte[1024 * 1024];
            for (var n = 0; n <= ExcelService.MaxExpandedBytes / block.Length; n++) stream.Write(block);
        }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en"], ["title", "A"]);
        using (var book = new XLWorkbook(FilePath)) { book.Worksheet(1).Cell(1, ExcelService.MaxColumns + 1).Value = "Too many"; book.Save(); }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en", "de", "fr", "ja", "id"]);
        using (var book = new XLWorkbook(FilePath)) { book.Worksheet(1).Cell(ExcelService.MaxRows, 1).Value = "Too many units"; book.Save(); }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
    }

    [Fact]
    public void AmbiguousWorkspaceEntriesBlockAndMetadataOnlyRowsPreserveValues()
    {
        var item = Item("main", "en", "Original", ResourcePathSegment.Named("title"));
        var unit = new SpreadsheetUnit(2, "main", "title", "en", null, "Reviewed", true);
        var table = new SpreadsheetTable("neutral", [unit]);
        var preview = SpreadsheetPlanner.Plan(table, [item]);
        Assert.Equal(1, preview.MetadataOnly); Assert.Equal("Original", Assert.Single(preview.Items).Value);
        var duplicate = ResourceEntryOperations.Copy(item, duplicate: true);
        Assert.Single(SpreadsheetPlanner.Plan(table, [item, duplicate]).Conflicts);
        Assert.False(SpreadsheetPlanner.Plan(table, [item, duplicate]).CanApply);
    }

    [Fact]
    public void LimitsRejectOversizeArchiveAndWorksheetBeforePlanning()
    {
        using (var file = File.Create(FilePath)) file.SetLength(ExcelService.MaxFileBytes + 1);
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
        Book(["Key", "en"], ["title", "A"]);
        using (var book = new XLWorkbook(FilePath)) { book.Worksheet(1).Cell(ExcelService.MaxRows + 1, 1).Value = "too far"; book.Save(); }
        Assert.Throws<FormatException>(() => ExcelService.Read(FilePath));
    }
}
