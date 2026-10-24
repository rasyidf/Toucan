namespace Toucan.Core.Models;

/// <summary>Column mapping only. Workbook IO and workspace edits belong to the host.</summary>
public sealed record SpreadsheetDialect(string Id, string Label, string KeyHeader, bool ExplicitPresence = false);

public sealed record SpreadsheetUnit(int Row, string Package, string Key, string Language,
    string? Value, string? Comment, bool? Approved, IReadOnlyList<ResourcePathSegment>? Path = null);

public sealed record SpreadsheetTable(string Dialect, IReadOnlyList<SpreadsheetUnit> Units, bool HasPackageColumn = true);
