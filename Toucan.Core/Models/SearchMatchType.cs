namespace Toucan.Core.Models;

/// <summary>
/// Classifies how a search query matched a translation item.
/// </summary>
public enum SearchMatchType
{
    Exact,    // Score 1.0 — query equals namespace or value (case-insensitive)
    Prefix,   // Score 0.9 — namespace or value starts with query
    Contains, // Score 0.7 — namespace or value contains query as substring
    Fuzzy     // Score varies — trigram Jaccard similarity > 0.3
}
