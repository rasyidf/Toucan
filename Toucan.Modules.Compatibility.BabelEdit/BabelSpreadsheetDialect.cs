using Toucan.Core.Models;

namespace Toucan.Modules;

/// <summary>Independent mapping of the public package/key spreadsheet exchange headers.</summary>
public static class BabelSpreadsheetDialect
{
    public static readonly SpreadsheetDialect Instance = new("babeledit-compatible", "BabelEdit-compatible (shared comments)", "Translation ID");
}
