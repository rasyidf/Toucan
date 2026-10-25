using ClosedXML.Excel;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class BabelExcelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("toucan-xlsx-").FullName;
    private string FilePath => Path.Combine(_folder, "translations.xlsx");
    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void RoundTripPreservesMixedApprovalsAndPackages()
    {
        var items = new List<TranslationItem>();
        foreach (var package in new[] { "main", "checkout" })
            foreach (var language in new[] { "en", "de" })
                items.Add(new TranslationItem { Namespace = package + ".title", Language = language,
                    Value = language == "en" ? "日本語 😀\nHello" : "", Comment = "Translator note", IsApproved = language == "en",
                    FormatData = new() { ["babel.package"] = package, ["babel.key"] = "title" } });
        ExcelService.Export(FilePath, items);
        using (var book = new XLWorkbook(FilePath))
        {
            Assert.Equal("Package", book.Worksheet(1).Cell(1, 1).GetString());
            Assert.Equal("de approved", book.Worksheet(1).Cell(1, 4).GetString());
        }
        var back = ExcelService.Import(FilePath);
        Assert.Equal(4, back.Count);
        foreach (var expected in items)
        {
            var item = Assert.Single(back, i => i.Namespace == expected.Namespace && i.Language == expected.Language);
            Assert.Equal(expected.Value, item.Value);
            Assert.Equal(expected.Comment, item.Comment);
            Assert.Equal(expected.IsApproved, item.IsApproved);
            Assert.Equal(expected.FormatData!["babel.package"], item.FormatData!["babel.package"]);
        }
    }

    [Fact]
    public void LegacyToucanWorkbookStillImports()
    {
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Translations");
            var headers = new[] { "Key", "en", "de", "Comment", "Approved" };
            for (var col = 0; col < headers.Length; col++) sheet.Cell(1, col + 1).Value = headers[col];
            sheet.Cell(2, 1).Value = "title";
            sheet.Cell(2, 2).Value = "Hello";
            sheet.Cell(2, 3).Value = "Hallo";
            sheet.Cell(2, 4).Value = "Note";
            sheet.Cell(2, 5).Value = "Yes";
            book.SaveAs(FilePath);
        }
        Assert.All(ExcelService.Import(FilePath), i => { Assert.True(i.IsApproved); Assert.Equal("Note", i.Comment); });
    }

    [Fact]
    public void ActualBabelEditExportImportsWithoutPseudoLanguages()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Toucan.CrossPlatform.slnx"))) root = Path.GetDirectoryName(root)!;
        var items = ExcelService.Import(Path.Combine(root, "samples", "babeledit-parity", "babeledit-export.xlsx"));
        Assert.Equal(22, items.Count);
        Assert.Equal(new[] { "de-DE", "en-US" }, items.Select(i => i.Language).Distinct().Order().ToArray());
        Assert.Equal("First", items.Single(i => i.Namespace == "choices.0" && i.Language == "en-US").Value);
        Assert.All(items, i => { Assert.False(i.IsApproved); Assert.Equal("main", i.FormatData!["babel.package"]); });
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("en")]
    public void UnknownSchemaOrDuplicateHeadersAreRejected(string firstHeader)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Translations");
        sheet.Cell(1, 1).Value = firstHeader;
        sheet.Cell(1, 2).Value = "en";
        book.SaveAs(FilePath);
        Assert.Throws<FormatException>(() => ExcelService.Import(FilePath));
    }
}
