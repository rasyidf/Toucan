namespace Toucan.Core.Models;

/// <summary>How a search matches text and which items it looks at.</summary>
/// <param name="MatchCase">Case-sensitive comparison (default is case-insensitive).</param>
/// <param name="WholeWord">Only match whole words.</param>
/// <param name="UseRegex">Treat the query as a regular expression.</param>
/// <param name="PreserveCase">When replacing, follow the case of the matched text (UPPER, lower, Capitalized).</param>
/// <param name="Languages">Language codes to search; empty means all.</param>
/// <param name="IncludeKeys">Key patterns (<c>*</c> wildcard, a plain word is a prefix) to search; empty means all.</param>
/// <param name="ExcludeKeys">Key patterns to skip.</param>
public sealed record SearchOptions(
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseRegex = false,
    bool PreserveCase = false,
    IReadOnlyList<string>? Languages = null,
    IReadOnlyList<string>? IncludeKeys = null,
    IReadOnlyList<string>? ExcludeKeys = null)
{
    /// <summary>Splits "a, b;c" into trimmed non-empty parts.</summary>
    public static IReadOnlyList<string> ParseList(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
