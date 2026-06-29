using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Computes three-way diffs between base (last save), mine (in-memory), and theirs (disk),
/// and applies non-conflicting changes automatically.
/// </summary>
public class DiffMergeEngine : IDiffMergeEngine
{
    /// <inheritdoc/>
    public DiffResult ComputeDiff(
        IReadOnlyList<TranslationItem> baseSnapshot,
        IReadOnlyList<TranslationItem> mine,
        IReadOnlyList<TranslationItem> theirs)
    {
        var baseMap = ToDictionary(baseSnapshot);
        var mineMap = ToDictionary(mine);
        var theirsMap = ToDictionary(theirs);

        // Collect all unique keys across all three sets
        var allKeys = new HashSet<(string Language, string Namespace)>();
        foreach (var key in baseMap.Keys) allKeys.Add(key);
        foreach (var key in mineMap.Keys) allKeys.Add(key);
        foreach (var key in theirsMap.Keys) allKeys.Add(key);

        var entries = new List<DiffEntry>();

        foreach (var key in allKeys)
        {
            var inBase = baseMap.TryGetValue(key, out var baseItem);
            var inMine = mineMap.TryGetValue(key, out var mineItem);
            var inTheirs = theirsMap.TryGetValue(key, out var theirsItem);

            var baseValue = inBase ? baseItem!.Value : null;
            var mineValue = inMine ? mineItem!.Value : null;
            var theirsValue = inTheirs ? theirsItem!.Value : null;

            var category = CategorizeEntry(inBase, inMine, inTheirs, baseValue, mineValue, theirsValue);

            if (category is not null)
            {
                entries.Add(new DiffEntry(
                    key.Language,
                    key.Namespace,
                    category.Value,
                    baseValue,
                    mineValue,
                    theirsValue));
            }
        }

        return new DiffResult(entries);
    }

    /// <inheritdoc/>
    public MergeResult ApplyNonConflicting(DiffResult diff, ITranslationManagementService target)
    {
        var conflicts = new List<DiffEntry>();
        int autoApplied = 0;

        var addedItems = new List<TranslationItem>();

        foreach (var entry in diff.Entries)
        {
            switch (entry.Category)
            {
                case DiffCategory.AddedOnDisk:
                    addedItems.Add(new TranslationItem
                    {
                        Language = entry.Language,
                        Namespace = entry.Namespace,
                        Value = entry.TheirsValue ?? string.Empty
                    });
                    autoApplied++;
                    break;

                case DiffCategory.ModifiedOnDisk:
                    ApplyModification(entry, target);
                    autoApplied++;
                    break;

                case DiffCategory.DeletedOnDisk:
                    target.RemoveItems(item =>
                        string.Equals(item.Language, entry.Language, StringComparison.Ordinal) &&
                        string.Equals(item.Namespace, entry.Namespace, StringComparison.Ordinal));
                    autoApplied++;
                    break;

                case DiffCategory.Conflicting:
                    conflicts.Add(entry);
                    break;
            }
        }

        if (addedItems.Count > 0)
        {
            target.AddItems(addedItems);
        }

        return new MergeResult(conflicts, autoApplied);
    }

    /// <summary>
    /// Categorizes a diff entry based on presence and value changes across the three sets.
    /// Returns null if the item is unchanged (no diff to report).
    /// </summary>
    private static DiffCategory? CategorizeEntry(
        bool inBase, bool inMine, bool inTheirs,
        string? baseValue, string? mineValue, string? theirsValue)
    {
        // Item present in theirs but absent in base → AddedOnDisk
        if (!inBase && inTheirs)
        {
            // If also present in mine with a different value, it's conflicting
            if (inMine && !string.Equals(mineValue, theirsValue, StringComparison.Ordinal))
            {
                return DiffCategory.Conflicting;
            }

            return DiffCategory.AddedOnDisk;
        }

        // Item absent in theirs but present in base → potential DeletedOnDisk
        if (inBase && !inTheirs)
        {
            bool mineChangedFromBase = inMine && !string.Equals(mineValue, baseValue, StringComparison.Ordinal);

            if (mineChangedFromBase)
            {
                // Mine changed it, theirs deleted it → Conflicting
                return DiffCategory.Conflicting;
            }

            return DiffCategory.DeletedOnDisk;
        }

        // Item present in both base and theirs
        if (inBase && inTheirs)
        {
            bool theirsChanged = !string.Equals(theirsValue, baseValue, StringComparison.Ordinal);
            bool mineChanged = inMine && !string.Equals(mineValue, baseValue, StringComparison.Ordinal);

            if (theirsChanged && mineChanged)
            {
                return DiffCategory.Conflicting;
            }

            if (theirsChanged && !mineChanged)
            {
                return DiffCategory.ModifiedOnDisk;
            }

            // If only mine changed or neither changed, there's no entry to report
            // (mine-only changes don't produce diff entries — the diff is about theirs vs base)
            return null;
        }

        // Item not in base, not in theirs — only in mine (no diff to report)
        return null;
    }

    /// <summary>
    /// Updates the value of a ModifiedOnDisk item in the target's Translations collection.
    /// </summary>
    private static void ApplyModification(DiffEntry entry, ITranslationManagementService target)
    {
        var item = target.Translations.FirstOrDefault(t =>
            string.Equals(t.Language, entry.Language, StringComparison.Ordinal) &&
            string.Equals(t.Namespace, entry.Namespace, StringComparison.Ordinal));

        if (item is not null)
        {
            item.Value = entry.TheirsValue ?? string.Empty;
        }
    }

    /// <summary>
    /// Builds a dictionary keyed by (Language, Namespace) for fast lookups.
    /// If duplicates exist, the last item wins.
    /// </summary>
    private static Dictionary<(string Language, string Namespace), TranslationItem> ToDictionary(
        IReadOnlyList<TranslationItem> items)
    {
        var dict = new Dictionary<(string Language, string Namespace), TranslationItem>(items.Count);

        foreach (var item in items)
        {
            dict[(item.Language, item.Namespace)] = item;
        }

        return dict;
    }
}
