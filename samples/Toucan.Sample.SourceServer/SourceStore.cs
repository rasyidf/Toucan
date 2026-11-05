using System.Globalization;

namespace Toucan.Sample.SourceServer;

/// <summary>One translation as the server holds it.</summary>
public sealed record StoredUnit(string Resource, string Name, string KeyId, string Language, string Value, string Revision, string Status, string? Comment);

public sealed record StoredChange(long Sequence, StoredUnit? Unit, string? DeletedResource, string? DeletedName, string? DeletedKeyId, string? DeletedLanguage);

/// <summary>The outcome of one write, in the protocol's words.</summary>
public sealed record OperationOutcome(Guid Id, string Outcome, string? Revision = null, string? RemoteId = null, StoredUnit? Current = null,
    string? ErrorCode = null, string? ErrorMessage = null);

/// <summary>
/// The in-memory string service behind the reference server. Revisions are a global counter, keys have stable IDs that survive
/// renames, applied operation IDs are remembered (idempotency) and every change is logged for the delta endpoint.
/// </summary>
public sealed class SourceStore
{
    private sealed class Key(string id, string resource, string name) { public string Id { get; } = id; public string Resource { get; set; } = resource; public string Name { get; set; } = name; }
    private sealed class Cell { public string Value = string.Empty; public string Revision = string.Empty; public string Status = "draft"; public string? Comment; }

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Key> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<(string KeyId, string Language), Cell> _cells = [];
    private readonly Dictionary<Guid, OperationOutcome> _applied = [];
    private readonly List<StoredChange> _log = [];
    private long _revision;
    private int _nextKey;

    public string ProjectId { get; } = "demo";
    public string ProjectName { get; } = "Demo project";
    public string SourceLanguage { get; } = "en";
    public IReadOnlyList<string> Languages { get; } = ["en", "fr", "de"];
    public IReadOnlyList<(string Id, string Name)> Resources { get; } = [("app", "Application strings")];
    public int PageSize { get; set; } = 200;

    public int MaxBatchSize => 100;

    // ───────────── reads ─────────────

    public IReadOnlyList<StoredUnit> Units(IReadOnlyCollection<string>? resources = null, IReadOnlyCollection<string>? languages = null)
    {
        lock (_gate)
            return [.. _cells.Select(c => ToUnit(c.Key.KeyId, c.Key.Language, c.Value))
                .Where(u => (resources is not { Count: > 0 } || resources.Contains(u.Resource)) && (languages is not { Count: > 0 } || languages.Contains(u.Language)))
                .OrderBy(u => u.Resource, StringComparer.Ordinal).ThenBy(u => u.Name, StringComparer.Ordinal).ThenBy(u => u.Language, StringComparer.Ordinal)];
    }

    public (IReadOnlyList<StoredChange> Changes, long Cursor) ChangesSince(long cursor)
    {
        lock (_gate) return ([.. _log.Where(c => c.Sequence > cursor)], _log.Count == 0 ? cursor : _log[^1].Sequence);
    }

    public long Cursor { get { lock (_gate) return _log.Count == 0 ? 0 : _log[^1].Sequence; } }

    // ───────────── test and seed helpers ─────────────

    public void Reset()
    {
        lock (_gate)
        {
            foreach (var entry in _cells.ToList()) LogDelete(_keys[entry.Key.KeyId], entry.Key.Language);
            _keys.Clear();
            _cells.Clear();
            _applied.Clear();
        }
    }

    public StoredUnit Put(string resource, string name, string language, string value, string status = "draft")
    {
        lock (_gate)
        {
            var key = FindKey(resource, name) ?? NewKey(resource, name);
            var cell = _cells.TryGetValue((key.Id, language), out var existing) ? existing : _cells[(key.Id, language)] = new Cell();
            cell.Value = value;
            cell.Status = status;
            cell.Revision = Next();
            var unit = ToUnit(key.Id, language, cell);
            _log.Add(new(_log.Count + 1, unit, null, null, null, null));
            return unit;
        }
    }

    public StoredUnit? Get(string resource, string name, string language)
    {
        lock (_gate) return FindKey(resource, name) is { } key && _cells.TryGetValue((key.Id, language), out var cell) ? ToUnit(key.Id, language, cell) : null;
    }

    public int CountKeys(string name)
    {
        lock (_gate) return _keys.Values.Count(k => k.Name == name);
    }

    // ───────────── writes ─────────────

    public IReadOnlyList<OperationOutcome> Apply(IEnumerable<WriteOperation> operations)
    {
        lock (_gate) return [.. operations.Select(ApplyOne)];
    }

    private OperationOutcome ApplyOne(WriteOperation op)
    {
        if (_applied.TryGetValue(op.Id, out var done)) return done with { Outcome = "alreadyApplied" };
        var result = Run(op);
        // Only an operation that took effect is remembered; a refusal can be retried after the cause is fixed.
        if (result.Outcome is "applied") _applied[op.Id] = result;
        return result;
    }

    private OperationOutcome Run(WriteOperation op)
    {
        switch (op.Type)
        {
            case "createKey":
            {
                if (op.Key is null || string.IsNullOrWhiteSpace(op.Key.Name)) return Refused(op.Id, "A key needs a name.");
                if (FindKey(op.Key.Resource, op.Key.Name) is { } existing)
                    return new(op.Id, "conflict", Current: _cells.Where(c => c.Key.KeyId == existing.Id).Select(c => ToUnit(existing.Id, c.Key.Language, c.Value)).FirstOrDefault(),
                        ErrorCode: "conflict", ErrorMessage: "The key exists.");
                var key = NewKey(op.Key.Resource, op.Key.Name);
                string? revision = null;
                foreach (var (language, value) in op.Values ?? [])
                {
                    var unit = Put(key.Resource, key.Name, language, value);
                    revision = unit.Revision;
                }
                return new(op.Id, "applied", revision, key.Id);
            }
            case "renameKey":
            {
                var key = op.Key is null ? null : FindKeyByIdOrName(op.Key);
                if (key is null) return new(op.Id, "notFound", ErrorCode: "not_found", ErrorMessage: "No such key.");
                if (string.IsNullOrWhiteSpace(op.NewName) || FindKey(key.Resource, op.NewName) is not null) return new(op.Id, "conflict", ErrorCode: "conflict", ErrorMessage: "The new name is taken.");
                key.Name = op.NewName;
                string? revision = null;
                foreach (var entry in _cells.Where(c => c.Key.KeyId == key.Id).ToList())
                {
                    entry.Value.Revision = revision = Next();
                    _log.Add(new(_log.Count + 1, ToUnit(key.Id, entry.Key.Language, entry.Value), null, null, null, null));
                }
                return new(op.Id, "applied", revision, key.Id);
            }
            case "deleteKey":
            {
                var key = op.Key is null ? null : FindKeyByIdOrName(op.Key);
                if (key is null) return new(op.Id, "notFound", ErrorCode: "not_found", ErrorMessage: "No such key.");
                foreach (var entry in _cells.Keys.Where(c => c.KeyId == key.Id).ToList()) { LogDelete(key, entry.Language); _cells.Remove(entry); }
                _keys.Remove(key.Id);
                return new(op.Id, "applied");
            }
        }

        // Operations on one translation.
        var target = op.Key is null ? null : FindKeyByIdOrName(op.Key);
        if (target is null || op.Language is null || !_cells.TryGetValue((target.Id, op.Language), out var cell))
            return new(op.Id, "notFound", ErrorCode: "not_found", ErrorMessage: "No such translation.");
        if (op.ExpectedRevision is not null && op.ExpectedRevision != cell.Revision)
            return new(op.Id, "conflict", Current: ToUnit(target.Id, op.Language, cell), ErrorCode: "conflict", ErrorMessage: "The translation changed.");
        switch (op.Type)
        {
            case "putValue":
                cell.Value = op.Value ?? string.Empty;
                break;
            case "putStatus":
                if (!Meanings.Contains(op.Status ?? string.Empty)) return Refused(op.Id, $"'{op.Status}' is not a review state.");
                cell.Status = op.Status!;
                break;
            case "putComment":
                cell.Comment = op.Comment;
                break;
            case "deleteTranslation":
                LogDelete(target, op.Language);
                _cells.Remove((target.Id, op.Language));
                return new(op.Id, "applied");
            default:
                return Refused(op.Id, $"Unknown operation '{op.Type}'.");
        }
        cell.Revision = Next();
        _log.Add(new(_log.Count + 1, ToUnit(target.Id, op.Language, cell), null, null, null, null));
        return new(op.Id, "applied", cell.Revision);
    }

    private static readonly HashSet<string> Meanings = new(["untranslated", "draft", "needsReview", "reviewed", "approved", "rejected"], StringComparer.Ordinal);

    private static OperationOutcome Refused(Guid id, string message) => new(id, "refused", ErrorCode: "invalid", ErrorMessage: message);

    private Key NewKey(string resource, string name)
    {
        var key = new Key("k_" + (++_nextKey).ToString(CultureInfo.InvariantCulture), resource, name);
        _keys[key.Id] = key;
        return key;
    }

    private Key? FindKey(string resource, string name) => _keys.Values.FirstOrDefault(k => k.Resource == resource && k.Name == name);

    private Key? FindKeyByIdOrName(KeyRef key) =>
        key.Id is not null && _keys.TryGetValue(key.Id, out var byId) ? byId : FindKey(key.Resource, key.Name);

    private string Next() => (++_revision).ToString(CultureInfo.InvariantCulture);

    private StoredUnit ToUnit(string keyId, string language, Cell cell) =>
        new(_keys[keyId].Resource, _keys[keyId].Name, keyId, language, cell.Value, cell.Revision, cell.Status, cell.Comment);

    private void LogDelete(Key key, string language) =>
        _log.Add(new(_log.Count + 1, null, key.Resource, key.Name, key.Id, language));
}

public sealed record KeyRef(string Resource, string Name, string? Id);

public sealed record WriteOperation(Guid Id, string Type, KeyRef? Key, string? Language, string? Value, string? Status, string? Comment,
    Dictionary<string, string>? Values, string? NewName, string? ExpectedRevision);
