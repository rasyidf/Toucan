using ClosedXML.Excel;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class ExcelService
{
    public static readonly SpreadsheetDialect Toucan = new("toucan-v2", "Toucan (presence and typed paths)", "Key", true);
    // Toucan itself emitted this unmarked schema before v2. Its public headers also work in other editors.
    public static readonly SpreadsheetDialect LegacyPackage = new("toucan-package-v1", "Legacy package workbook", "Translation ID");
    public const int MaxRows = 50_000, MaxColumns = 256, MaxUnits = 200_000;
    public const long MaxFileBytes = 20 * 1024 * 1024, MaxExpandedBytes = 100 * 1024 * 1024;

    /// <summary>Historical API, retained for callers of Toucan's previous spreadsheet service.</summary>
    public static void Export(string filePath, IEnumerable<TranslationItem> translations) =>
        Write(filePath, translations.Select(LegacySpreadsheetBridge.Normalize), LegacyPackage);

    public static List<TranslationItem> Import(string filePath) => LegacySpreadsheetBridge.Items(Read(filePath));

    public static void Write(string filePath, IEnumerable<TranslationItem> translations, SpreadsheetDialect dialect)
    {
        var items = translations.ToList();
        var languages = items.Select(t => t.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (languages.Count == 0) throw new FormatException("There are no translations to export.");
        foreach (var language in languages) ValidateLanguage(language);
        var groups = items.GroupBy(t => (Package: Package(t), Key: Key(t), Path: dialect.ExplicitPresence ? t.Identity?.KeyToken ?? "" : "")).ToList();
        if (!dialect.ExplicitPresence && groups.Any(g => g.Where(i => i.Identity != null).Select(i => i.Identity!.KeyToken).Distinct().Count() > 1))
            throw new FormatException("This dialect cannot distinguish literal IDs from nested paths. Choose Toucan export.");
        var stride = dialect.ExplicitPresence ? 4 : 2;
        var firstLanguage = dialect.ExplicitPresence ? 4 : 3;
        var columns = firstLanguage - 1 + languages.Count * stride + (dialect.ExplicitPresence ? 0 : 1);
        if (groups.Count + 1 > MaxRows || columns > MaxColumns || items.Count > MaxUnits) throw new FormatException("Workbook exceeds the spreadsheet exchange limits.");
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Translations");
        Text(ws.Cell(1, 1), "Package"); Text(ws.Cell(1, 2), dialect.KeyHeader);
        if (dialect.ExplicitPresence) Text(ws.Cell(1, 3), "Path");
        for (var i = 0; i < languages.Count; i++)
        {
            var col = firstLanguage + i * stride;
            Text(ws.Cell(1, col), languages[i]);
            Text(ws.Cell(1, col + 1), languages[i] + (dialect.ExplicitPresence ? " present" : " approved"));
            if (dialect.ExplicitPresence) { Text(ws.Cell(1, col + 2), languages[i] + " comment"); Text(ws.Cell(1, col + 3), languages[i] + " approved"); }
        }
        if (!dialect.ExplicitPresence) Text(ws.Cell(1, columns), "Comment");
        var row = 2;
        foreach (var group in groups.OrderBy(g => g.Key.Package, StringComparer.Ordinal).ThenBy(g => g.Key.Key, StringComparer.Ordinal).ThenBy(g => g.Key.Path, StringComparer.Ordinal))
        {
            Text(ws.Cell(row, 1), group.Key.Package); Text(ws.Cell(row, 2), group.Key.Key);
            if (dialect.ExplicitPresence) Text(ws.Cell(row, 3), group.Key.Path);
            if (group.GroupBy(t => t.Language).Any(g => g.Count() > 1)) throw new FormatException($"Ambiguous duplicate '{group.Key.Key}' in package '{group.Key.Package}'.");
            if (!dialect.ExplicitPresence && group.Count() != languages.Count)
                throw new FormatException("This dialect cannot represent omitted language units. Choose Toucan export.");
            if (!dialect.ExplicitPresence && group.Select(t => t.Comment ?? "").Distinct().Count() > 1)
                throw new FormatException("This dialect has one shared comment per key. Choose Toucan export to preserve language-specific comments.");
            for (var i = 0; i < languages.Count; i++)
            {
                var item = group.SingleOrDefault(t => t.Language == languages[i]);
                var col = firstLanguage + i * stride;
                Text(ws.Cell(row, col), item?.Value ?? "");
                if (dialect.ExplicitPresence)
                {
                    ws.Cell(row, col + 1).Value = item != null;
                    if (item != null) { Text(ws.Cell(row, col + 2), item.Comment ?? ""); ws.Cell(row, col + 3).Value = item.IsApproved; }
                }
                else ws.Cell(row, col + 1).Value = item?.IsApproved ?? false;
            }
            if (!dialect.ExplicitPresence) Text(ws.Cell(row, columns), group.First().Comment ?? "");
            row++;
        }
        ws.Columns().Width = 24;
        ws.SheetView.FreezeRows(1);
        workbook.SaveAs(filePath);
    }

    public static SpreadsheetTable Read(string filePath, IEnumerable<SpreadsheetDialect>? dialects = null)
    {
        ValidateArchive(filePath);
        try { return ReadWorkbook(filePath, dialects); }
        catch (Exception ex) when (ex is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or System.Xml.XmlException or InvalidDataException)
        { throw new FormatException("Malformed XLSX workbook.", ex); }
    }

    private static SpreadsheetTable ReadWorkbook(string filePath, IEnumerable<SpreadsheetDialect>? dialects)
    {
        using var workbook = new XLWorkbook(filePath);
        if (workbook.Worksheets.Count != 1) throw new FormatException("Expected exactly one translation worksheet.");
        var ws = workbook.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        if (lastRow > MaxRows || lastCol > MaxColumns) throw new FormatException("Workbook exceeds the spreadsheet row/column limits.");
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= lastCol; col++)
        {
            var header = Cell(ws, 1, col).Trim();
            if (header.Length == 0 || !headers.TryAdd(header, col)) throw new FormatException("Spreadsheet headers must be nonempty and unique.");
        }
        var native = headers.ContainsKey("Path");
        var schemas = new[] { Toucan, LegacyPackage, new SpreadsheetDialect("toucan-v1", "Legacy Toucan", "Key") }
            .Concat(dialects ?? []).DistinctBy(d => (d.KeyHeader, d.ExplicitPresence)).ToList();
        var matches = schemas.Where(d => d.ExplicitPresence == native && headers.ContainsKey(d.KeyHeader)).ToList();
        if (matches.Count != 1 || headers.ContainsKey("Key") && headers.ContainsKey("Translation ID")) throw new FormatException("Unknown or ambiguous spreadsheet schema.");
        var schema = matches[0];
        var keyColumn = headers[schema.KeyHeader];
        var packageColumn = headers.GetValueOrDefault("Package");
        if (native && packageColumn == 0) throw new FormatException("Toucan workbooks require Package and Path columns.");
        var commentColumn = headers.GetValueOrDefault("Comment");
        var globalApproved = headers.GetValueOrDefault("Approved");
        var reserved = new HashSet<string>([schema.KeyHeader, "Package", "Path", "Comment", "Approved"], StringComparer.OrdinalIgnoreCase);
        var languages = headers.Where(h => !reserved.Contains(h.Key) && !Suffix(h.Key, out _)).ToList();
        if (languages.Count == 0) throw new FormatException("The spreadsheet has no language columns.");
        foreach (var language in languages) ValidateLanguage(language.Key);
        foreach (var header in headers.Keys.Where(h => Suffix(h, out _)))
        {
            Suffix(header, out var parent);
            if (!headers.ContainsKey(parent) || !languages.Any(l => l.Key.Equals(parent, StringComparison.OrdinalIgnoreCase)) ||
                !native && !header.EndsWith(" approved", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"Unknown header '{header}' or missing language column.");
        }
        if (native && (commentColumn != 0 || globalApproved != 0)) throw new FormatException("Toucan v2 uses language-specific metadata columns.");
        if (native && languages.Any(l => !headers.ContainsKey(l.Key + " present"))) throw new FormatException("Every Toucan language column requires a 'present' column.");
        if ((long)(lastRow - 1) * languages.Count > MaxUnits) throw new FormatException("Workbook exceeds the spreadsheet unit limit.");
        var results = new List<SpreadsheetUnit>();
        var seen = new HashSet<(string, string, string, string)>();
        for (var row = 2; row <= lastRow; row++)
        {
            var key = Cell(ws, row, keyColumn); // IDs are exact; whitespace can be part of a key.
            if (key.Length == 0)
            {
                if (Enumerable.Range(1, lastCol).Any(c => Cell(ws, row, c).Length != 0)) throw new FormatException($"Row {row} has data but no key.");
                continue;
            }
            var package = packageColumn == 0 ? "main" : Cell(ws, row, packageColumn);
            if (string.IsNullOrWhiteSpace(package)) throw new FormatException($"Row {row} has no package name.");
            IReadOnlyList<ResourcePathSegment>? path = null;
            if (native && Cell(ws, row, headers["Path"]) is { Length: > 0 } token)
            {
                try { path = JsonSerializer.Deserialize<ResourcePathSegment[]>(token); }
                catch (JsonException ex) { throw new FormatException($"Row {row} has an invalid typed path.", ex); }
                if (path is not { Count: > 0 and <= 128 } || path.Any(p => p is null || (p.Property == null) == (p.Index == null) || p.Index < 0) || ResourceEntryOperations.Selector(path) != key)
                    throw new FormatException($"Row {row} path does not match the key or contains invalid segments.");
            }
            foreach (var language in languages)
            {
                var identity = (package, key, path == null ? "" : JsonSerializer.Serialize(path), language.Key);
                if (!seen.Add(identity)) throw new FormatException($"Duplicate row {row}: {package}/{key} [{language.Key}].");
                // In v2 false omits the entire language unit. A true blank is an explicit clear.
                if (native && !ReadApproved(Cell(ws, row, headers[language.Key + " present"]))) continue;
                var approval = headers.GetValueOrDefault(language.Key + " approved", globalApproved);
                var comment = headers.GetValueOrDefault(language.Key + " comment", commentColumn);
                results.Add(new(row, package, key, language.Key, Cell(ws, row, language.Value),
                    comment == 0 ? null : Cell(ws, row, comment), approval == 0 ? null : ReadApproved(Cell(ws, row, approval)), path));
            }
        }
        return new(schema.Id, results, packageColumn != 0);
    }

    internal static string Package(TranslationItem item) => item.Identity?.Package ?? "main";
    internal static string Key(TranslationItem item) => item.Identity is { } identity ? ResourceEntryOperations.Selector(identity.Path) : item.Namespace;
    private static bool Suffix(string header, out string language)
    {
        foreach (var suffix in new[] { " approved", " present", " comment" })
            if (header.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { language = header[..^suffix.Length]; return true; }
        language = ""; return false;
    }
    private static void ValidateLanguage(string language)
    {
        if (language.Length is < 2 or > 35 || language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new FormatException($"Unknown header or invalid language '{language}'.");
        try { if (CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName.Length != 2) throw new CultureNotFoundException("Unknown language."); }
        catch (CultureNotFoundException) { throw new FormatException($"Unknown header or invalid language '{language}'."); }
        // ICU accepts arbitrary syntactically valid names; require a known base language.
        if (!CultureInfo.GetCultures(CultureTypes.NeutralCultures).Any(c => c.Name.Equals(language.Split('-')[0], StringComparison.OrdinalIgnoreCase)))
            throw new FormatException($"Unknown header or language '{language}'.");
    }
    private static string Cell(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.HasFormula) throw new FormatException($"Formula at row {row}, column {col}; exchange cells must contain text or flags.");
        return cell.GetString();
    }
    private static void Text(IXLCell cell, string value)
    {
        if (value.Length > 32_767) throw new FormatException("Spreadsheet text exceeds Excel's 32,767-character cell limit.");
        cell.Style.NumberFormat.Format = "@";
        // ClosedXML consumes one leading apostrophe as a text marker. Escape it, then remove the marker style.
        cell.Value = value.StartsWith('\'') ? "'" + value : value;
        cell.Style.IncludeQuotePrefix = false;
    }
    private static bool ReadApproved(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "1" => true, "FALSE" or "NO" or "0" or "" => false,
        _ => throw new FormatException($"Invalid flag '{value}'. Expected true/false or yes/no.")
    };
    private static void ValidateArchive(string file)
    {
        if (new FileInfo(file).Length > MaxFileBytes) throw new FormatException("Workbook exceeds the compressed size limit.");
        try
        {
            using var archive = ZipFile.OpenRead(file);
            if (archive.GetEntry("[Content_Types].xml") == null || archive.GetEntry("xl/workbook.xml") == null)
                throw new FormatException("The archive is not an XLSX workbook.");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total = checked(total + entry.Length);
                if (archive.Entries.Count > 2048 || total > MaxExpandedBytes) throw new FormatException("Workbook exceeds the expanded size limit.");
            }
        }
        catch (InvalidDataException ex) { throw new FormatException("Not a valid XLSX archive.", ex); }
    }
}
