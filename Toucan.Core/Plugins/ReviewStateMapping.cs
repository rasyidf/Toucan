using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>Converts between the plugin API's <see cref="ReviewState"/> and the approved / needs-review flags Core stores.</summary>
internal static class ReviewStateMapping
{
    public static ReviewState ToState(bool approved, bool needsReview) =>
        approved ? ReviewState.Approved : needsReview ? ReviewState.NeedsReview : ReviewState.Draft;

    public static (bool Approved, bool NeedsReview) ToFlags(ReviewState state) => state switch
    {
        ReviewState.Approved => (true, false),
        ReviewState.NeedsReview => (false, true),
        _ => (false, false),
    };

    /// <summary>What a plugin built for <paramref name="apiVersion"/> may see: <see cref="ReviewState.NeedsReview"/> only from 1.3.</summary>
    public static ReviewState ForApi(ReviewState state, Version apiVersion) =>
        state == ReviewState.NeedsReview && apiVersion < NeedsReviewSince ? ReviewState.Draft : state;

    public static Version NeedsReviewSince { get; } = new(1, 3);
}
