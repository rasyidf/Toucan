namespace Toucan.Plugins.Testing;

/// <summary>
/// A small project held in memory that implements <see cref="IWorkspaceApi"/> with the same rules as the application:
/// edits are checked on a copy first and applied all or nothing (unless <see cref="WorkspaceEdit.AllowPartial"/>), a stale
/// <see cref="WorkspaceEdit.BasedOnRevision"/> or a changed <c>ExpectedValue</c> refuses, and every applied edit raises the
/// revision. It does not model undo, validation or the approval policy; those belong to the application.
/// </summary>
public sealed class InMemoryWorkspace : IWorkspaceApi
{
    private record struct Cell(string Value, string Comment, ReviewState Review, bool Modified);

    private readonly object _gate = new();
    private SortedDictionary<string, Dictionary<string, Cell>>? _keys;
    private List<string> _languages = [];
    private string _primary = string.Empty;
    private string _id = "test-workspace";
    private string _path = string.Empty;
    private long _revision;

    public bool IsOpen
    {
        get { lock (_gate) return _keys is not null; }
    }

    public event EventHandler? Changed;

    /// <summary>Opens a project. <paramref name="values"/> is key → language → text.</summary>
    public void Open(string primaryLanguage, IEnumerable<string> languages, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values,
        string workspaceId = "test-workspace", string projectPath = "/test/project")
    {
        lock (_gate)
        {
            _primary = primaryLanguage;
            _languages = [.. languages];
            _id = workspaceId;
            _path = projectPath;
            _keys = new SortedDictionary<string, Dictionary<string, Cell>>(StringComparer.Ordinal);
            foreach (var (key, byLanguage) in values) _keys[key] = NewRow(byLanguage);
            _revision++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the project, as when the user closes it.</summary>
    public void Close()
    {
        lock (_gate) _keys = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes a value as the user would by typing, so tests can provoke stale and conflicting edits.</summary>
    public void UserEdit(string key, string language, string value)
    {
        lock (_gate)
        {
            _keys![key][language] = _keys[key][language] with { Value = value, Modified = true };
            _revision++;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_keys is null) return Task.FromResult<WorkspaceSnapshot?>(null);
            var units = _keys.SelectMany(k => _languages.Select(l => (k.Key, Language: l, Cell: k.Value[l])))
                .Select(u => new UnitSnapshot(u.Key, u.Language, u.Cell.Value, u.Cell.Comment, u.Cell.Review, u.Cell.Modified))
                .ToList();
            return Task.FromResult<WorkspaceSnapshot?>(new WorkspaceSnapshot(_id, _path, _primary, [.. _languages], units, _revision));
        }
    }

    public Task<EditResult> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        cancellationToken.ThrowIfCancellationRequested();
        EditResult result;
        lock (_gate)
        {
            if (_keys is null) return Task.FromResult(EditResult.Refused(_revision, new EditIssue(-1, EditIssueKind.NoProject, "No project is open.")));
            if (edit.BasedOnRevision is { } based && based != _revision)
                return Task.FromResult(EditResult.Refused(_revision, new EditIssue(-1, EditIssueKind.Stale, "The project changed since the snapshot was taken.")));

            var copy = new SortedDictionary<string, Dictionary<string, Cell>>(
                _keys.ToDictionary(k => k.Key, k => new Dictionary<string, Cell>(k.Value)), StringComparer.Ordinal);
            var issues = new List<EditIssue>();
            var changed = new HashSet<(string, string)>();
            for (var i = 0; i < edit.Operations.Count; i++)
            {
                if (Run(copy, edit.Operations[i], i, changed) is { } issue) issues.Add(issue);
            }

            if (issues.Count > 0 && !edit.AllowPartial)
                return Task.FromResult(EditResult.Refused(_revision, [.. issues]));

            _keys = copy;
            if (changed.Count > 0 || edit.Operations.Count > 0) _revision++;
            result = new EditResult(true, changed.Count, _revision, issues, []);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(result);
    }

    private EditIssue? Run(SortedDictionary<string, Dictionary<string, Cell>> keys, EditOperation op, int index, HashSet<(string, string)> changed)
    {
        switch (op)
        {
            case SetValue s:
                if (Locate(keys, s.Key, s.Language, index) is { } missing) return missing;
                if (s.ExpectedValue is { } expected && keys[s.Key][s.Language].Value != expected)
                    return new EditIssue(index, EditIssueKind.Conflict, $"'{s.Key}' ({s.Language}) was changed since it was read.");
                keys[s.Key][s.Language] = keys[s.Key][s.Language] with { Value = s.Value, Modified = true };
                changed.Add((s.Key, s.Language));
                return null;
            case SetComment c:
                if (Locate(keys, c.Key, c.Language, index) is { } m2) return m2;
                keys[c.Key][c.Language] = keys[c.Key][c.Language] with { Comment = c.Comment, Modified = true };
                changed.Add((c.Key, c.Language));
                return null;
            case SetReview r:
                if (Locate(keys, r.Key, r.Language, index) is { } m3) return m3;
                keys[r.Key][r.Language] = keys[r.Key][r.Language] with { Review = r.State, Modified = true };
                changed.Add((r.Key, r.Language));
                return null;
            case AddKey a:
                if (string.IsNullOrWhiteSpace(a.Key) || a.Key.StartsWith('.') || a.Key.EndsWith('.')) return new EditIssue(index, EditIssueKind.InvalidKey, $"'{a.Key}' is not a valid key.");
                if (keys.ContainsKey(a.Key)) return new EditIssue(index, EditIssueKind.KeyExists, $"'{a.Key}' already exists.");
                keys[a.Key] = NewRow(a.Values ?? new Dictionary<string, string>());
                foreach (var language in _languages) changed.Add((a.Key, language));
                return null;
            case RenameKey rn:
                if (!keys.ContainsKey(rn.Key)) return new EditIssue(index, EditIssueKind.UnknownKey, $"'{rn.Key}' does not exist.");
                if (string.IsNullOrWhiteSpace(rn.NewKey)) return new EditIssue(index, EditIssueKind.InvalidKey, $"'{rn.NewKey}' is not a valid key.");
                var moves = keys.Keys.Where(k => k == rn.Key || k.StartsWith(rn.Key + ".", StringComparison.Ordinal)).ToList();
                if (moves.Any(k => keys.ContainsKey(rn.NewKey + k[rn.Key.Length..]) && !moves.Contains(rn.NewKey + k[rn.Key.Length..])))
                    return new EditIssue(index, EditIssueKind.KeyExists, $"'{rn.NewKey}' already exists.");
                foreach (var k in moves)
                {
                    var row = keys[k];
                    keys.Remove(k);
                    keys[rn.NewKey + k[rn.Key.Length..]] = row;
                }
                return null;
            case DeleteKey d:
                if (!keys.ContainsKey(d.Key)) return new EditIssue(index, EditIssueKind.UnknownKey, $"'{d.Key}' does not exist.");
                foreach (var k in keys.Keys.Where(k => k == d.Key || k.StartsWith(d.Key + ".", StringComparison.Ordinal)).ToList()) keys.Remove(k);
                return null;
            default:
                return new EditIssue(index, EditIssueKind.Empty, "Unsupported step.");
        }
    }

    private EditIssue? Locate(SortedDictionary<string, Dictionary<string, Cell>> keys, string key, string language, int index)
    {
        if (!keys.ContainsKey(key)) return new EditIssue(index, EditIssueKind.UnknownKey, $"'{key}' does not exist.");
        if (!_languages.Contains(language)) return new EditIssue(index, EditIssueKind.UnknownLanguage, $"'{language}' is not a language of this project.");
        return null;
    }

    private Dictionary<string, Cell> NewRow(IReadOnlyDictionary<string, string> values) =>
        _languages.ToDictionary(l => l, l => new Cell(values.GetValueOrDefault(l, string.Empty), string.Empty, ReviewState.Draft, false));
}
