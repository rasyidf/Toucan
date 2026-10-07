namespace Toucan.Core.Models;

/// <summary>
/// A single search hit within a translation item's value, including match position
/// and optional replacement preview.
/// </summary>
public class SearchResultItem
{
    public required string Key { get; init; }
    public required string Language { get; init; }
    public required string Value { get; init; }
    public int MatchStart { get; init; }
    public int MatchLength { get; init; }

    /// <summary>True when the match is in the key itself; those are shown but never replaced.</summary>
    public bool InKey { get; init; }

    /// <summary>The matched text substring.</summary>
    public string MatchText => Value.Substring(MatchStart, MatchLength);

    /// <summary>Preview of the value after replacement (populated by PreviewReplace).</summary>
    public string? ReplacedValue { get; set; }

    /// <summary>What this one match would become (populated by PreviewReplace); lets a UI show the change inline.</summary>
    public string? ReplacementText { get; set; }
}
