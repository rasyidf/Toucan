using ClosedXML.Excel;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class ExcelService
{
    public static void Export(string filePath, IEnumerable<TranslationItem> translations)
    {
        var items = translations.ToList();
        var languages = items.Select(t => t.Language).Distinct().OrderBy(l => l).ToList();
        var namespaces = items.Select(t => t.Namespace).Distinct().OrderBy(n => n).ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Translations");

        ws.Cell(1, 1).Value = "Key";
        for (int i = 0; i < languages.Count; i++)
            ws.Cell(1, i + 2).Value = languages[i];
        ws.Cell(1, languages.Count + 2).Value = "Comment";
        ws.Cell(1, languages.Count + 3).Value = "Approved";

        for (int row = 0; row < namespaces.Count; row++)
        {
            var ns = namespaces[row];
            ws.Cell(row + 2, 1).Value = ns;
            for (int col = 0; col < languages.Count; col++)
            {
                var value = items.FirstOrDefault(t => t.Namespace == ns && t.Language == languages[col])?.Value ?? "";
                ws.Cell(row + 2, col + 2).Value = value;
            }
            var first = items.FirstOrDefault(t => t.Namespace == ns);
            ws.Cell(row + 2, languages.Count + 2).Value = first?.Comment ?? "";
            ws.Cell(row + 2, languages.Count + 3).Value = first?.IsApproved == true ? "Yes" : "";
        }

        ws.Columns().AdjustToContents(1, 50);
        workbook.SaveAs(filePath);
    }

    public static List<TranslationItem> Import(string filePath)
    {
        var results = new List<TranslationItem>();
        using var workbook = new XLWorkbook(filePath);
        var ws = workbook.Worksheet(1);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

        var languages = new List<string>();
        for (int col = 2; col <= lastCol; col++)
        {
            var header = ws.Cell(1, col).GetString().Trim();
            if (header == "Comment" || header == "Approved") break;
            languages.Add(header);
        }

        int commentCol = languages.Count + 2;
        int approvedCol = languages.Count + 3;

        for (int row = 2; row <= lastRow; row++)
        {
            var ns = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrEmpty(ns)) continue;
            var comment = commentCol <= lastCol ? ws.Cell(row, commentCol).GetString() : "";
            var approved = approvedCol <= lastCol && ws.Cell(row, approvedCol).GetString().Equals("Yes", StringComparison.OrdinalIgnoreCase);
            for (int col = 0; col < languages.Count; col++)
            {
                var value = ws.Cell(row, col + 2).GetString();
                results.Add(new TranslationItem { Namespace = ns, Language = languages[col], Value = value, Comment = comment, IsApproved = approved });
            }
        }

        return results;
    }
}
