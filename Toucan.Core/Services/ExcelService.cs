using ClosedXML.Excel;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class ExcelService
{
    private static string Package(TranslationItem item) => item.FormatData?.GetValueOrDefault("babel.package") ?? "main";
    private static string Key(TranslationItem item) => item.FormatData?.GetValueOrDefault("babel.key") ?? item.Namespace;

    public static void Export(string filePath, IEnumerable<TranslationItem> translations)
    {
        var items = translations.ToList();
        var languages = items.Select(t => t.Language).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Translations");
        ws.Cell(1, 1).Value = "Package";
        ws.Cell(1, 2).Value = "Translation ID";
        for (var i = 0; i < languages.Count; i++)
        {
            ws.Cell(1, 3 + i * 2).Value = languages[i];
            ws.Cell(1, 4 + i * 2).Value = languages[i] + " approved";
        }
        var commentColumn = 3 + languages.Count * 2;
        ws.Cell(1, commentColumn).Value = "Comment";
        var row = 2;
        foreach (var group in items.GroupBy(t => (Package: Package(t), Key: Key(t))).OrderBy(g => g.Key.Package, StringComparer.Ordinal).ThenBy(g => g.Key.Key, StringComparer.Ordinal))
        {
            ws.Cell(row, 1).Value = group.Key.Package;
            ws.Cell(row, 2).Value = group.Key.Key;
            for (var i = 0; i < languages.Count; i++)
            {
                var item = group.SingleOrDefault(t => t.Language == languages[i]);
                ws.Cell(row, 3 + i * 2).Value = item?.Value ?? string.Empty;
                ws.Cell(row, 4 + i * 2).Value = item?.IsApproved ?? false;
            }
            ws.Cell(row, commentColumn).Value = group.First().Comment;
            row++;
        }
        ws.Columns().AdjustToContents(1, 50);
        workbook.SaveAs(filePath);
    }

    public static List<TranslationItem> Import(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var ws = workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= lastCol; col++)
        {
            var header = ws.Cell(1, col).GetString().Trim();
            if (header.Length == 0 || !headers.TryAdd(header, col))
                throw new FormatException("Spreadsheet headers must be nonempty and unique.");
        }
        var babel = headers.TryGetValue("Translation ID", out var keyColumn);
        if (!babel && !headers.TryGetValue("Key", out keyColumn))
            throw new FormatException("Expected a Toucan Key column or a BabelEdit Translation ID column.");
        var packageColumn = headers.GetValueOrDefault("Package");
        var commentColumn = headers.GetValueOrDefault("Comment");
        var approvedColumn = headers.GetValueOrDefault("Approved");
        var languages = headers.Where(h => h.Key is not ("Key" or "Translation ID" or "Package" or "Comment" or "Approved") && !h.Key.EndsWith(" approved", StringComparison.OrdinalIgnoreCase))
            .Where(h => h.Value != keyColumn && h.Value != packageColumn && h.Value != commentColumn && h.Value != approvedColumn).ToList();
        if (languages.Count == 0) throw new FormatException("The spreadsheet has no language columns.");
        foreach (var header in headers.Keys.Where(h => h.EndsWith(" approved", StringComparison.OrdinalIgnoreCase)))
            if (!headers.ContainsKey(header[..^9])) throw new FormatException($"Approval column '{header}' has no language column.");
        var packageNames = Enumerable.Range(2, Math.Max(0, lastRow - 1))
            .Where(r => !string.IsNullOrWhiteSpace(ws.Cell(r, keyColumn).GetString()))
            .Select(r => packageColumn == 0 ? "main" : ws.Cell(r, packageColumn).GetString().Trim())
            .Distinct(StringComparer.Ordinal).ToList();
        var results = new List<TranslationItem>();
        var seen = new HashSet<(string Key, string Language)>();
        for (var row = 2; row <= lastRow; row++)
        {
            var key = ws.Cell(row, keyColumn).GetString().Trim();
            if (key.Length == 0) continue;
            var package = packageColumn == 0 ? "main" : ws.Cell(row, packageColumn).GetString().Trim();
            if (package.Length == 0) throw new FormatException($"Row {row} has no package name.");
            var ns = packageNames.Count > 1 ? package + "." + key : key;
            foreach (var language in languages)
            {
                if (!seen.Add((ns, language.Key))) throw new FormatException($"Duplicate translation ID '{ns}' for {language.Key}.");
                var approval = headers.GetValueOrDefault(language.Key + " approved", approvedColumn);
                results.Add(new TranslationItem
                {
                    Namespace = ns, Language = language.Key, Value = ws.Cell(row, language.Value).GetString(),
                    Comment = commentColumn == 0 ? string.Empty : ws.Cell(row, commentColumn).GetString(),
                    IsApproved = approval != 0 && ReadApproved(ws.Cell(row, approval).GetString()),
                    FormatData = new() { ["babel.package"] = package, ["babel.key"] = key,
                        ["exchange.comment"] = (commentColumn != 0).ToString(), ["exchange.approved"] = (approval != 0).ToString() }
                });
            }
        }
        return results;
    }

    private static bool ReadApproved(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "1" => true,
        "FALSE" or "NO" or "0" or "" => false,
        _ => throw new FormatException($"Invalid approval flag '{value}'. Expected true/false or yes/no.")
    };
}
