using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Manages undo/redo stacks for translation value edits. An undo step is one edit, or every edit made inside a
/// <see cref="BeginGroup"/> (a bulk operation undoes in one go).
/// </summary>
public interface IUndoRedoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Record(string ns, string language, string oldValue, string newValue);

    /// <summary>
    /// Opens a group: edits recorded until the returned object is disposed become a single undo step. Groups nest; only the
    /// outermost one makes a step, and a group with no edits makes none.
    /// </summary>
    IDisposable BeginGroup();

    /// <summary>Takes the latest step off the undo stack. Returns its edits in the order they were made (undo them last to first), or null when there is nothing to undo.</summary>
    IReadOnlyList<EditAction>? Undo();

    /// <summary>Puts the latest undone step back. Returns its edits in the order they were made, or null.</summary>
    IReadOnlyList<EditAction>? Redo();
    void Clear();
}
