using System.Collections.Generic;

using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Services;

/// <summary>
/// Simple undo/redo stack for translation value edits.
/// Registered as singleton in DI — injected wherever needed.
/// </summary>
public class UndoRedoService : IUndoRedoService
{
    private readonly Stack<EditAction> _undoStack = new();
    private readonly Stack<EditAction> _redoStack = new();
    private const int MaxHistory = 200;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Record(string ns, string language, string oldValue, string newValue)
    {
        if (oldValue == newValue)
        {
            return;
        }

        _undoStack.Push(new EditAction(ns, language, oldValue, newValue));
        _redoStack.Clear();
        // Cap history — keep newest MaxHistory items
        if (_undoStack.Count > MaxHistory)
        {
            EditAction[] temp = _undoStack.ToArray(); // newest-first (LIFO order)
            _undoStack.Clear();
            for (int i = MaxHistory - 1; i >= 0; i--)
            {
                _undoStack.Push(temp[i]);
            }
        }
    }

    public EditAction? Undo()
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        EditAction action = _undoStack.Pop();
        _redoStack.Push(action);
        return action;
    }

    public EditAction? Redo()
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        EditAction action = _redoStack.Pop();
        _undoStack.Push(action);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
