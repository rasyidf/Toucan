using System;
using System.Collections.Generic;
using System.Linq;

namespace Toucan.Services;

/// <summary>
/// A single undoable edit action.
/// </summary>
internal record EditAction(string Namespace, string Language, string OldValue, string NewValue);

/// <summary>
/// Simple undo/redo stack for translation value edits.
/// ponytail: global singleton, no framework — just two stacks.
/// Upgrade path: per-field granularity, grouping batch edits.
/// </summary>
internal class UndoRedoService
{
    private static readonly Lazy<UndoRedoService> s_instance = new(() => new UndoRedoService());
    public static UndoRedoService Instance => s_instance.Value;

    private readonly Stack<EditAction> _undoStack = new();
    private readonly Stack<EditAction> _redoStack = new();
    private const int MaxHistory = 200;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Record(string ns, string language, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        _undoStack.Push(new EditAction(ns, language, oldValue, newValue));
        _redoStack.Clear();
        // Cap history
        if (_undoStack.Count > MaxHistory)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < MaxHistory; i++)
                _undoStack.Push(temp[i]);
        }
    }

    public EditAction? Undo()
    {
        if (_undoStack.Count == 0) return null;
        var action = _undoStack.Pop();
        _redoStack.Push(action);
        return action;
    }

    public EditAction? Redo()
    {
        if (_redoStack.Count == 0) return null;
        var action = _redoStack.Pop();
        _undoStack.Push(action);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
