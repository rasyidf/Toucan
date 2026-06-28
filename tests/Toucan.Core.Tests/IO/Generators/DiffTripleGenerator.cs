using FsCheck;
using FsCheck.Fluent;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Tests.IO.Generators;

/// <summary>
/// Represents a triple of (base, mine, theirs) TranslationItem sets
/// for three-way diff property testing.
/// </summary>
public record DiffTriple(
    List<TranslationItem> Base,
    List<TranslationItem> Mine,
    List<TranslationItem> Theirs);

/// <summary>
/// FsCheck generator for (base, mine, theirs) TranslationItem sets with controlled overlap.
/// Produces triples that exercise all diff categories:
/// - AddedOnDisk: present in theirs, absent in base
/// - ModifiedOnDisk: changed in theirs vs base, unchanged in mine vs base
/// - DeletedOnDisk: absent in theirs, present in base, unchanged in mine vs base
/// - Conflicting: changed in both mine and theirs relative to base
/// </summary>
public static class DiffTripleGenerator
{
    private static Gen<string> ShortValueGen =>
        Gen.Choose(1, 30).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x61, 0x7A).Select(i => (char)i), length)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a DiffTriple with controlled overlap to cover all diff categories.
    /// </summary>
    public static Gen<DiffTriple> DiffTripleGen =>
        TranslationItemGenerator.UniqueItemListGen(10)
            .SelectMany(baseItems => BuildTriple(baseItems));

    /// <summary>
    /// Generates a DiffTriple that guarantees at least one entry per category.
    /// </summary>
    public static Gen<DiffTriple> FullCoverageDiffTripleGen =>
        Gen.Choose(4, 8).SelectMany(count =>
            Gen.ListOf(TranslationItemGenerator.SmallTranslationItemGen, count)
                .Select(items => DeduplicateByKey(items))
                .Where(items => items.Count >= 4)
                .SelectMany(uniqueBase => BuildFullCoverageTriple(uniqueBase)));

    /// <summary>
    /// Creates an Arbitrary for DiffTriple.
    /// </summary>
    public static Arbitrary<DiffTriple> DiffTripleArbitrary() =>
        Arb.From(DiffTripleGen);

    /// <summary>
    /// Builds a triple from a base set by randomly applying modifications
    /// to create mine and theirs variants.
    /// </summary>
    private static Gen<DiffTriple> BuildTriple(List<TranslationItem> baseItems)
    {
        if (baseItems.Count == 0)
        {
            // Generate some added-on-disk items
            return Gen.Choose(1, 3).SelectMany(n =>
                Gen.ListOf(TranslationItemGenerator.SmallTranslationItemGen, n)
                    .Select(added => new DiffTriple([], [], DeduplicateByKey(added))));
        }

        return Gen.ArrayOf(MineModificationGen, baseItems.Count).SelectMany(mineMods =>
            Gen.ArrayOf(TheirsModificationGen, baseItems.Count).SelectMany(theirsMods =>
                Gen.Choose(0, 3).SelectMany(addedCount =>
                    Gen.ListOf(TranslationItemGenerator.SmallTranslationItemGen, addedCount)
                        .Select(addedOnDisk =>
                            BuildTripleFromModifications(baseItems, mineMods, theirsMods, addedOnDisk)))));
    }

    /// <summary>
    /// Builds a triple that guarantees at least one item in each diff category.
    /// </summary>
    private static Gen<DiffTriple> BuildFullCoverageTriple(List<TranslationItem> baseItems)
    {
        return from modifiedValue in ShortValueGen
               from conflictMineValue in ShortValueGen
               from conflictTheirsValue in ShortValueGen
               from addedItem in TranslationItemGenerator.SmallTranslationItemGen
               select BuildFullCoverage(baseItems, modifiedValue, conflictMineValue,
                   conflictTheirsValue, addedItem);
    }

    private static DiffTriple BuildFullCoverage(
        List<TranslationItem> baseItems,
        string modifiedValue,
        string conflictMineValue,
        string conflictTheirsValue,
        TranslationItem addedItem)
    {
        var mine = new List<TranslationItem>();
        var theirs = new List<TranslationItem>();

        for (int i = 0; i < baseItems.Count && i < 4; i++)
        {
            var item = baseItems[i];
            switch (i % 4)
            {
                case 0:
                    // ModifiedOnDisk: unchanged in mine, changed in theirs
                    mine.Add(CloneItem(item));
                    theirs.Add(CloneItem(item, value: modifiedValue));
                    break;
                case 1:
                    // DeletedOnDisk: present in mine (unchanged), absent in theirs
                    mine.Add(CloneItem(item));
                    // Not added to theirs
                    break;
                case 2:
                    // Conflicting: changed in both mine and theirs
                    mine.Add(CloneItem(item, value: conflictMineValue));
                    theirs.Add(CloneItem(item, value: conflictTheirsValue));
                    break;
                case 3:
                    // Unchanged: same in all three
                    mine.Add(CloneItem(item));
                    theirs.Add(CloneItem(item));
                    break;
            }
        }

        // Any remaining base items are unchanged
        for (int i = 4; i < baseItems.Count; i++)
        {
            mine.Add(CloneItem(baseItems[i]));
            theirs.Add(CloneItem(baseItems[i]));
        }

        // AddedOnDisk: ensure the added item has a unique key
        var existingKeys = baseItems.Select(x => (x.Language, x.Namespace)).ToHashSet();
        if (!existingKeys.Contains((addedItem.Language, addedItem.Namespace)))
        {
            theirs.Add(addedItem);
        }
        else
        {
            // Create a guaranteed-unique item
            theirs.Add(new TranslationItem
            {
                Language = addedItem.Language,
                Namespace = $"added.{Guid.NewGuid():N}",
                Value = addedItem.Value,
                Comment = addedItem.Comment
            });
        }

        return new DiffTriple(baseItems, mine, theirs);
    }

    /// <summary>
    /// Modification type for the "mine" side: keep unchanged or modify value.
    /// </summary>
    private static Gen<MineModification> MineModificationGen =>
        Gen.Frequency(
            (7, Gen.Constant(MineModification.Unchanged)),
            (3, Gen.Constant(MineModification.ModifyValue)));

    /// <summary>
    /// Modification type for the "theirs" side: keep, modify, or delete.
    /// </summary>
    private static Gen<TheirsModification> TheirsModificationGen =>
        Gen.Frequency(
            (5, Gen.Constant(TheirsModification.Unchanged)),
            (3, Gen.Constant(TheirsModification.ModifyValue)),
            (2, Gen.Constant(TheirsModification.Delete)));

    private static DiffTriple BuildTripleFromModifications(
        List<TranslationItem> baseItems,
        MineModification[] mineModifications,
        TheirsModification[] theirsModifications,
        List<TranslationItem> addedOnDisk)
    {
        var mine = new List<TranslationItem>();
        var theirs = new List<TranslationItem>();
        var existingKeys = baseItems.Select(x => (x.Language, x.Namespace)).ToHashSet();

        for (int i = 0; i < baseItems.Count; i++)
        {
            var item = baseItems[i];

            // Build mine side
            switch (mineModifications[i])
            {
                case MineModification.Unchanged:
                    mine.Add(CloneItem(item));
                    break;
                case MineModification.ModifyValue:
                    mine.Add(CloneItem(item, value: item.Value + "_mine"));
                    break;
            }

            // Build theirs side
            switch (theirsModifications[i])
            {
                case TheirsModification.Unchanged:
                    theirs.Add(CloneItem(item));
                    break;
                case TheirsModification.ModifyValue:
                    theirs.Add(CloneItem(item, value: item.Value + "_theirs"));
                    break;
                case TheirsModification.Delete:
                    // Not added to theirs (simulates deletion on disk)
                    break;
            }
        }

        // Add items that only exist on disk (AddedOnDisk) — filter out key collisions
        foreach (var added in addedOnDisk)
        {
            if (!existingKeys.Contains((added.Language, added.Namespace)))
            {
                theirs.Add(added);
                existingKeys.Add((added.Language, added.Namespace));
            }
        }

        return new DiffTriple(baseItems, mine, theirs);
    }

    private static TranslationItem CloneItem(TranslationItem item, string? value = null, string? comment = null) =>
        new()
        {
            Language = item.Language,
            Namespace = item.Namespace,
            Value = value ?? item.Value,
            Comment = comment ?? item.Comment,
            ChangeType = item.ChangeType
        };

    private static List<TranslationItem> DeduplicateByKey(List<TranslationItem> items)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<TranslationItem>();

        foreach (var item in items)
        {
            if (seen.Add((item.Language, item.Namespace)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private enum MineModification { Unchanged, ModifyValue }
    private enum TheirsModification { Unchanged, ModifyValue, Delete }
}
