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

/// <summary>Undo/redo stack for translation value edits (capped at 200 steps; a step is one edit or one group of them).</summary>
public sealed class UndoRedoService : IUndoRedoService
{
    private const int MaxHistory = 200;
    private readonly LinkedList<IReadOnlyList<EditAction>> _undo = new();
    private readonly Stack<IReadOnlyList<EditAction>> _redo = new();
    private List<EditAction>? _group;
    private int _depth;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(string ns, string language, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        var action = new EditAction(ns, language, oldValue, newValue);
        if (_group is not null)
        {
            _group.Add(action);
            return;
        }
        Push([action]);
    }

    public IDisposable BeginGroup()
    {
        if (_depth++ == 0) _group = [];
        return new Group(this);
    }

    public void RecordEntry(TranslationItem before, TranslationItem after)
    {
        var action = new EditAction(before.Namespace, before.Language, before.Value, after.Value)
        {
            BeforeEntry = ResourceEntryOperations.Copy(before),
            AfterEntry = ResourceEntryOperations.Copy(after)
        };
        if (_group is not null) _group.Add(action);
        else Push([action]);
    }

    public void RecordStructure(IReadOnlyList<TranslationItem> before, IReadOnlyList<TranslationItem> after)
    {
        var action = new EditAction("", "", "", "")
        {
            BeforeStructure = before.Select(i => ResourceEntryOperations.Copy(i)).ToList(),
            AfterStructure = after.Select(i => ResourceEntryOperations.Copy(i)).ToList()
        };
        if (_group is not null) _group.Add(action);
        else Push([action]);
    }

    public void RecordMetadata(string ns, string language, Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var action = new EditAction(ns, language, "", "") { BeforeMetadata = new(before), AfterMetadata = new(after) };
        if (_group != null) _group.Add(action); else Push([action]);
    }

    public void RecordSpelling(SpellingSettings before, SpellingSettings after)
    {
        var action = new EditAction("", "", "", "") { BeforeSpelling = SpellingSettings.Copy(before), AfterSpelling = SpellingSettings.Copy(after) };
        if (_group != null) _group.Add(action); else Push([action]);
    }

    public void RecordConfiguration(ProjectSettings before, ProjectSettings after)
    {
        var action = new EditAction("", "", "", "")
        {
            BeforeConfiguration = Toucan.Core.Services.ProjectConfiguration.Copy(before),
            AfterConfiguration = Toucan.Core.Services.ProjectConfiguration.Copy(after)
        };
        if (_group is not null) _group.Add(action); else Push([action]);
    }

    private void EndGroup()
    {
        if (--_depth > 0) return;
        var edits = _group;
        _group = null;
        if (edits is { Count: > 0 }) Push(edits);
    }

    private void Push(IReadOnlyList<EditAction> step)
    {
        _undo.AddLast(step);
        _redo.Clear();
        while (_undo.Count > MaxHistory) _undo.RemoveFirst();
    }

    public IReadOnlyList<EditAction>? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        _redo.Push(node.Value);
        return node.Value;
    }

    public IReadOnlyList<EditAction>? Redo()
    {
        if (_redo.Count == 0) return null;
        var step = _redo.Pop();
        _undo.AddLast(step);
        return step;
    }

    private sealed class Group(UndoRedoService owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.EndGroup();
        }
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
