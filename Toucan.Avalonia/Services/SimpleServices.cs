using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Avalonia.Services;

public sealed class PreferenceService : IPreferenceService
{
    public AppOptions Load() => AppOptions.LoadFromDisk();
    public void Save(AppOptions options) => options.ToDisk();
}

/// <summary>Undo/redo stack for translation value edits (capped at 200 entries).</summary>
public sealed class UndoRedoService : IUndoRedoService
{
    private const int MaxHistory = 200;
    private readonly LinkedList<EditAction> _undo = new();
    private readonly Stack<EditAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(string ns, string language, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        _undo.AddLast(new EditAction(ns, language, oldValue, newValue));
        _redo.Clear();
        while (_undo.Count > MaxHistory) _undo.RemoveFirst();
    }

    public EditAction? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        _redo.Push(node.Value);
        return node.Value;
    }

    public EditAction? Redo()
    {
        if (_redo.Count == 0) return null;
        var action = _redo.Pop();
        _undo.AddLast(action);
        return action;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
