using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Manages undo/redo stacks for translation value edits.
/// </summary>
public interface IUndoRedoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Record(string ns, string language, string oldValue, string newValue);
    EditAction? Undo();
    EditAction? Redo();
    void Clear();
}
