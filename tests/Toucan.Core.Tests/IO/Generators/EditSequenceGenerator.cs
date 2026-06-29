using FsCheck;
using FsCheck.Fluent;

namespace Toucan.Core.Tests.IO.Generators;

/// <summary>
/// Represents a single operation in an edit sequence for property-based testing.
/// </summary>
public abstract record EditOperation;

/// <summary>Set a translation item's Value to a new string.</summary>
public record SetValue(int ItemIndex, string NewValue) : EditOperation;

/// <summary>Set a translation item's Comment to a new string.</summary>
public record SetComment(int ItemIndex, string NewComment) : EditOperation;

/// <summary>Undo the last edit operation.</summary>
public record UndoOp() : EditOperation;

/// <summary>Redo the last undone operation.</summary>
public record RedoOp() : EditOperation;

/// <summary>Mark all items as saved (updates baselines).</summary>
public record SaveOp() : EditOperation;

/// <summary>Mark specific items as saved.</summary>
public record MarkSavedOp(IReadOnlyList<int> ItemIndices) : EditOperation;

/// <summary>
/// A sequence of edit operations for testing dirty tracking consistency.
/// </summary>
public record EditSequence(IReadOnlyList<EditOperation> Operations);

/// <summary>
/// FsCheck generator for random edit/save/undo/redo sequences.
/// Used to test dirty tracking invariants under arbitrary operation orderings.
/// </summary>
public static class EditSequenceGenerator
{
    /// <summary>
    /// Generates a short random value string (for use in SetValue operations).
    /// </summary>
    private static Gen<string> ShortValueGen =>
        Gen.Choose(0, 50).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(i => (char)i), length)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a short random comment string (for use in SetComment operations).
    /// </summary>
    private static Gen<string> ShortCommentGen =>
        Gen.Choose(0, 100).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(i => (char)i), length)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a single EditOperation given the number of items available.
    /// </summary>
    /// <param name="itemCount">Number of items in the collection (for index bounds).</param>
    public static Gen<EditOperation> EditOperationGen(int itemCount)
    {
        if (itemCount <= 0)
        {
            // Only save/undo/redo are valid with no items
            return Gen.OneOf(
                Gen.Constant<EditOperation>(new UndoOp()),
                Gen.Constant<EditOperation>(new RedoOp()),
                Gen.Constant<EditOperation>(new SaveOp()));
        }

        var setValueGen =
            from idx in Gen.Choose(0, itemCount - 1)
            from value in ShortValueGen
            select (EditOperation)new SetValue(idx, value);

        var setCommentGen =
            from idx in Gen.Choose(0, itemCount - 1)
            from comment in ShortCommentGen
            select (EditOperation)new SetComment(idx, comment);

        var undoGen = Gen.Constant<EditOperation>(new UndoOp());
        var redoGen = Gen.Constant<EditOperation>(new RedoOp());
        var saveGen = Gen.Constant<EditOperation>(new SaveOp());

        var markSavedGen =
            Gen.Choose(1, Math.Min(itemCount, 5)).SelectMany(count =>
                Gen.ArrayOf(Gen.Choose(0, itemCount - 1), count)
                    .Select(indices => (EditOperation)new MarkSavedOp(indices.Distinct().ToList())));

        // Weight towards edits (more realistic sequences)
        return Gen.Frequency(
            (4, setValueGen),
            (2, setCommentGen),
            (2, undoGen),
            (2, redoGen),
            (1, saveGen),
            (1, markSavedGen));
    }

    /// <summary>
    /// Generates an EditSequence of 1-30 operations for a given item count.
    /// </summary>
    /// <param name="itemCount">Number of items in the collection.</param>
    public static Gen<EditSequence> EditSequenceGen(int itemCount) =>
        Gen.Choose(1, 30).SelectMany(length =>
            Gen.ArrayOf(EditOperationGen(itemCount), length)
                .Select(ops => new EditSequence(ops)));

    /// <summary>
    /// Generates a short EditSequence (1-10 operations) for fast tests.
    /// </summary>
    public static Gen<EditSequence> ShortEditSequenceGen(int itemCount) =>
        Gen.Choose(1, 10).SelectMany(length =>
            Gen.ArrayOf(EditOperationGen(itemCount), length)
                .Select(ops => new EditSequence(ops)));

    /// <summary>
    /// Creates an Arbitrary for EditSequence given a fixed item count.
    /// </summary>
    public static Arbitrary<EditSequence> EditSequenceArbitrary(int itemCount) =>
        Arb.From(EditSequenceGen(itemCount));
}
