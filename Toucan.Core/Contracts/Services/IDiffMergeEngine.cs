using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Categorizes a difference found during three-way diff.</summary>
public enum DiffCategory
{
    /// <summary>Item present in theirs but absent in base.</summary>
    AddedOnDisk,

    /// <summary>Item changed in theirs vs base, unchanged in mine vs base.</summary>
    ModifiedOnDisk,

    /// <summary>Item absent in theirs, present in base, unchanged in mine vs base.</summary>
    DeletedOnDisk,

    /// <summary>Item changed in both mine and theirs relative to base.</summary>
    Conflicting
}

/// <summary>A single difference entry from a three-way diff.</summary>
public record DiffEntry(
    string Language,
    string Namespace,
    DiffCategory Category,
    string? BaseValue,
    string? MineValue,
    string? TheirsValue);

/// <summary>Result of computing a three-way diff.</summary>
public record DiffResult(IReadOnlyList<DiffEntry> Entries);

/// <summary>Result of applying non-conflicting changes from a diff.</summary>
public record MergeResult(IReadOnlyList<DiffEntry> Conflicts, int AutoApplied);

/// <summary>
/// Computes three-way diffs between base (last save), mine (in-memory), and theirs (disk),
/// and applies non-conflicting changes automatically.
/// </summary>
public interface IDiffMergeEngine
{
    /// <summary>Computes a three-way diff between base (last save), mine (in-memory), and theirs (disk).</summary>
    DiffResult ComputeDiff(
        IReadOnlyList<TranslationItem> baseSnapshot,
        IReadOnlyList<TranslationItem> mine,
        IReadOnlyList<TranslationItem> theirs);

    /// <summary>Applies non-conflicting changes and returns conflicts for user resolution.</summary>
    MergeResult ApplyNonConflicting(DiffResult diff, ITranslationManagementService target);
}
