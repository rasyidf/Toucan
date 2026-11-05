using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Keeps approvals honest: an approval records the source text it was given for, and is withdrawn
/// (to <see cref="ReviewStatus.NeedsReview"/>) when that source or the target text changes.
/// </summary>
public static class ReviewTracker
{
    /// <summary>Short stable fingerprint of a source text. Null and empty text hash the same.</summary>
    public static string Hash(string? text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>Approves <paramref name="target"/> against the source text it was reviewed with.</summary>
    public static void Approve(TranslationItem target, string? sourceValue, DateTime? nowUtc = null)
    {
        target.IsApproved = true;
        target.NeedsReview = false;
        target.ApprovedAtUtc = nowUtc ?? DateTime.UtcNow;
        target.ReviewedSourceHash = Hash(sourceValue);
    }

    /// <summary>Withdraws an approval after an edit. Items that were never approved stay drafts.</summary>
    public static bool Invalidate(TranslationItem target)
    {
        if (!target.IsApproved) return false;
        target.IsApproved = false;
        target.ApprovedAtUtc = null;
        target.NeedsReview = true;
        return true;
    }

    /// <summary>Clears the approval and review flags entirely (for example when a value is emptied or reset).</summary>
    public static void Reset(TranslationItem target)
    {
        target.IsApproved = false;
        target.ApprovedAtUtc = null;
        target.NeedsReview = false;
        target.ReviewedSourceHash = null;
    }

    /// <summary>
    /// Compares every approved target with the current source text of its key. Approvals recorded
    /// before review tracking existed get the current source as their baseline; approvals whose source
    /// changed (for example in a file edited outside Toucan) become needs-review.
    /// </summary>
    /// <returns>Number of approvals withdrawn.</returns>
    public static int Reconcile(IEnumerable<TranslationItem> items, string sourceLanguage)
    {
        var all = items as IReadOnlyList<TranslationItem> ?? items.ToList();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in all)
            if (item.Language == sourceLanguage) sources[item.Namespace] = item.Value;

        var withdrawn = 0;
        foreach (var item in all)
        {
            if (item.Language == sourceLanguage || !item.IsApproved) continue;
            var current = Hash(sources.GetValueOrDefault(item.Namespace));
            if (item.ReviewedSourceHash is null) item.ReviewedSourceHash = current;
            else if (item.ReviewedSourceHash != current && Invalidate(item)) withdrawn++;
        }
        return withdrawn;
    }

    /// <summary>
    /// Call after the source text of a key changed: every approved target of that key needs another review.
    /// </summary>
    /// <returns>Number of approvals withdrawn.</returns>
    public static int OnSourceChanged(IEnumerable<TranslationItem> items, string sourceLanguage, string @namespace)
    {
        var withdrawn = 0;
        foreach (var item in items)
            if (item.Language != sourceLanguage && item.Namespace == @namespace && Invalidate(item)) withdrawn++;
        return withdrawn;
    }
}
