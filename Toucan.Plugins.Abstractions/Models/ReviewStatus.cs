namespace Toucan.Core.Models;

/// <summary>Where a translation stands in review.</summary>
public enum ReviewStatus
{
    /// <summary>Not yet reviewed.</summary>
    Draft = 0,

    /// <summary>Was approved (or submitted) but the source or target changed since, so it needs another look.</summary>
    NeedsReview = 1,

    /// <summary>Approved against the source text it was reviewed with.</summary>
    Approved = 2,
}
