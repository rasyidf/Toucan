using System.Collections.Immutable;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services;

/// <summary>
/// Structural edits whose persistence needs a chosen document. Choices are unit state, so undo/redo and recovery keep them
/// with the translation; the save planner validates them against the framework layout before anything is written.
/// </summary>
public sealed partial class WorkspaceService
{
    private ImmutableHashSet<string> _removedLanguages = ImmutableHashSet<string>.Empty;

    /// <summary>Adds an entry in every language the family can place. Values default to empty.</summary>
    public EditResult AddDocumentEntry(WorkspaceDestinationFamily family, TranslationPath keyPath,
        IReadOnlyDictionary<string, string>? values = null, string originId = "editor")
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(keyPath);
        EditResult result;
        lock (_gate)
        {
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            if (_source is null) return Refused(EditIssueKind.InvalidKey, "Only document workspaces accept destination choices.");
            if (keyPath.Segments.Count == 0 || keyPath.Segments.Any(string.IsNullOrEmpty)) return Refused(EditIssueKind.InvalidKey, "Key path cannot be empty.");
            if (family.Documents.Count == 0) return Refused(EditIssueKind.UnknownLanguage, "The destination has no document the framework can place.");
            var next = _state.ToBuilder();
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            foreach (var (language, path) in family.Documents)
            {
                if (OccupiedLocked(next.Values, path, family.Namespace, keyPath, exclude: null))
                    return Refused(EditIssueKind.KeyExists, "Destination key already exists.");
                next[(id, language)] = new(id, language, KeyOf(family.Namespace, keyPath), family.Namespace, keyPath,
                    values?.GetValueOrDefault(language) ?? string.Empty, string.Empty, false, now, null, ChangeType.DirectEdit, null, null, path);
            }
            result = CommitStructureLocked(next.ToImmutable(), originId, family.Documents.Count);
        }
        RaiseChanged();
        return result;
    }

    /// <summary>Moves an entry to another family; languages the family cannot place are refused rather than dropped.</summary>
    public EditResult MoveDocumentEntry(Guid entryId, WorkspaceDestinationFamily family, TranslationPath? keyPath = null, string originId = "editor")
    {
        ArgumentNullException.ThrowIfNull(family);
        EditResult result;
        lock (_gate)
        {
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            if (_source is null) return Refused(EditIssueKind.InvalidKey, "Only document workspaces accept destination choices.");
            var units = _state.Values.Where(u => u.Id == entryId).ToList();
            if (units.Count == 0) return Refused(EditIssueKind.UnknownKey, "Entry does not exist.");
            if (units.FirstOrDefault(u => !family.Documents.ContainsKey(u.Language)) is { } unplaced)
                return Refused(EditIssueKind.UnknownLanguage, $"The destination has no '{unplaced.Language}' document.");
            var next = _state.ToBuilder();
            foreach (var unit in units)
            {
                var path = family.Documents[unit.Language];
                var target = keyPath ?? unit.KeyPath;
                if (OccupiedLocked(next.Values, path, family.Namespace, target, exclude: entryId))
                    return Refused(EditIssueKind.KeyExists, "Destination key already exists.");
                // Staying in the original document is a rename; the binding already routes it.
                var destination = unit.Binding is { } binding && DocumentPathLocked(binding.DocumentId) == path ? null : path;
                next[(unit.Id, unit.Language)] = unit with { Namespace = family.Namespace, KeyPath = target, Key = KeyOf(family.Namespace, target), Destination = destination };
            }
            result = CommitStructureLocked(next.ToImmutable(), originId, units.Count);
        }
        RaiseChanged();
        return result;
    }

    /// <summary>
    /// Adds empty translations for every entry whose family can place <paramref name="language"/>. Entries in families without
    /// a describable destination are reported, not silently skipped.
    /// </summary>
    public EditResult AddDocumentLanguage(string language, IReadOnlyList<WorkspaceDestinationFamily> families, string originId = "editor")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(families);
        EditResult result;
        lock (_gate)
        {
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            if (_source is null) return Refused(EditIssueKind.InvalidKey, "Only document workspaces accept destination choices.");
            var next = _state.ToBuilder();
            var unplaced = 0;
            var added = 0;
            foreach (var entry in _state.Values.GroupBy(u => u.Id))
            {
                if (entry.Any(u => u.Language == language)) continue;
                var paths = entry.Select(UnitPathLocked).ToHashSet(StringComparer.Ordinal);
                var family = families.FirstOrDefault(f => f.Documents.Values.Any(paths.Contains));
                if (family is null || !family.Documents.TryGetValue(language, out var path)) { unplaced++; continue; }
                var template = entry.First();
                next[(template.Id, language)] = new(template.Id, language, template.Key, template.Namespace, template.KeyPath, string.Empty,
                    string.Empty, false, null, null, ChangeType.DirectEdit, null, null, path);
                added++;
            }
            if (added == 0 && unplaced > 0)
                return Refused(EditIssueKind.UnknownLanguage, "The framework cannot describe where this language's documents belong.");
            _removedLanguages = _removedLanguages.Remove(language);
            result = CommitStructureLocked(next.ToImmutable(), originId, added);
            if (unplaced > 0)
                result = result with { Issues = [new(-1, EditIssueKind.UnknownLanguage, $"{unplaced} entr{(unplaced == 1 ? "y has" : "ies have")} no '{language}' destination.")] };
        }
        RaiseChanged();
        return result;
    }

    /// <summary>Removes a language. Saving deletes its source documents unless undo restores its translations first.</summary>
    public EditResult RemoveDocumentLanguage(string language, string originId = "editor")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        EditResult result;
        lock (_gate)
        {
            if (_settings is null || _closing) return Refused(EditIssueKind.NoProject, "No editable project is open.");
            if (_source is null) return Refused(EditIssueKind.InvalidKey, "Only document workspaces accept destination choices.");
            var removed = _state.Where(p => p.Value.Language == language).Select(p => p.Key).ToList();
            if (removed.Count == 0 && _source.Documents.All(d => d.Language != language))
                return Refused(EditIssueKind.UnknownLanguage, "The language is not in this workspace.");
            _removedLanguages = _removedLanguages.Add(language);
            result = CommitStructureLocked(_state.RemoveRange(removed), originId, removed.Count);
            // An already-empty language changes no units, yet its documents still need deleting.
            if (removed.Count == 0) { _revision++; PublishChangeLocked(WorkspaceChangeKind.Updated, WorkspaceChangeOrigin.Native, originId); }
        }
        RaiseChanged();
        return result;
    }

    /// <summary>The pending document chosen for an entry's translation, or null when its binding routes it.</summary>
    public string? PendingDestination(Guid entryId, string language)
    {
        lock (_gate) return _state.TryGetValue((entryId, language), out var unit) ? unit.Destination : null;
    }

    private IReadOnlyList<DocumentId> RemovedLanguageDocumentsLocked()
    {
        if (_source is null || _removedLanguages.IsEmpty) return [];
        var live = _state.Values.Select(u => u.Language).ToHashSet(StringComparer.Ordinal);
        return _source.Documents.Where(d => _removedLanguages.Contains(d.Language) && !live.Contains(d.Language)).Select(d => d.Id).ToArray();
    }

    private EditResult CommitStructureLocked(ImmutableDictionary<(Guid, string), Unit> next, string originId, int changed)
    {
        Commit(next, WorkspaceChangeOrigin.Native, originId);
        PublishProjections();
        return new(true, changed, _revision, [], Findings(_state));
    }

    private bool OccupiedLocked(IEnumerable<Unit> units, string path, TranslationPath ns, TranslationPath keyPath, Guid? exclude) =>
        units.Any(u => u.Id != exclude && UnitPathLocked(u) == path && u.Namespace.Equals(ns) && Conflicts(u.KeyPath, keyPath));

    private string? UnitPathLocked(Unit unit) => unit.Destination ?? (unit.Binding is { } binding ? DocumentPathLocked(binding.DocumentId) : null);
    private string? DocumentPathLocked(DocumentId id) => _source?.Documents.FirstOrDefault(d => d.Id == id)?.RelativePath;
    private static string KeyOf(TranslationPath ns, TranslationPath keyPath) => string.Join('.', ns.Segments.Concat(keyPath.Segments));
}
