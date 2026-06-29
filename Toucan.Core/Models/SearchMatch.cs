namespace Toucan.Core.Models;

/// <summary>
/// Represents a single search result with its relevance score and match type.
/// </summary>
public record SearchMatch(TranslationItem Item, double Score, SearchMatchType MatchType);
