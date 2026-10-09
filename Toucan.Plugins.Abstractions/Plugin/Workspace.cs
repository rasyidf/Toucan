namespace Toucan.Plugins;

/// <summary>Where a translation is in review. More states arrive with the review workflow; code that reads one should allow for that.</summary>
public enum ReviewState
{
    Draft,
    Approved,
}

/// <summary>One key in one language, as it was when the snapshot was taken. <c>IsModified</c> means changed since the project was opened or last saved.</summary>
public sealed record UnitSnapshot(string Key, string Language, string Value, string Comment, ReviewState Review, bool IsModified);

/// <summary>
/// An immutable copy of the open project. It never changes, so a plugin can work on it for as long as it likes and then
/// say what to change; <see cref="Revision"/> lets the host notice that the project moved on meanwhile.
/// </summary>
public sealed class WorkspaceSnapshot
{
    private readonly Dictionary<(string Key, string Language), UnitSnapshot> _index;

    public WorkspaceSnapshot(string workspaceId, string projectPath, string primaryLanguage, IReadOnlyList<string> languages,
        IReadOnlyList<UnitSnapshot> units, long revision)
    {
        WorkspaceId = workspaceId;
        ProjectPath = projectPath;
        PrimaryLanguage = primaryLanguage;
        Languages = languages;
        Units = units;
        Revision = revision;
        _index = units.ToDictionary(u => (u.Key, u.Language));
    }

    public string WorkspaceId { get; }
    public string ProjectPath { get; }
    public string PrimaryLanguage { get; }
    public IReadOnlyList<string> Languages { get; }
    public IReadOnlyList<UnitSnapshot> Units { get; }

    /// <summary>Changes with every edit, by the user or a plugin. Pass it as <see cref="WorkspaceEdit.BasedOnRevision"/> to refuse an edit made on stale data.</summary>
    public long Revision { get; }

    public IEnumerable<string> Keys => Units.Select(u => u.Key).Distinct(StringComparer.Ordinal);

    public UnitSnapshot? Find(string key, string language) => _index.GetValueOrDefault((key, language));
}

/// <summary>One step of a <see cref="WorkspaceEdit"/>. Steps run in order, and later ones see the effect of earlier ones.</summary>
public abstract record EditOperation;

/// <summary>
/// Sets a translation's text. A plain edit like typing it: one undo step, dirty tracking, saved with the project. When
/// <c>ExpectedValue</c> is set, the edit is refused unless the translation still has that text (the text the plugin saw).
/// </summary>
public sealed record SetValue(string Key, string Language, string Value, string? ExpectedValue = null) : EditOperation;

public sealed record SetComment(string Key, string Language, string Comment) : EditOperation;

/// <summary>Moves a translation between review states. Approving obeys the project's strict-approval policy.</summary>
public sealed record SetReview(string Key, string Language, ReviewState State) : EditOperation;

/// <summary>Adds a key in every language of the project, empty except for the values given (by language).</summary>
public sealed record AddKey(string Key, IReadOnlyDictionary<string, string>? Values = null) : EditOperation;

/// <summary>Renames a key and every key below it. Not undoable, like renaming in the editor.</summary>
public sealed record RenameKey(string Key, string NewKey) : EditOperation;

/// <summary>Deletes a key and every key below it in every language. Not undoable, like deleting in the editor.</summary>
public sealed record DeleteKey(string Key) : EditOperation;

/// <summary>
/// A set of changes applied together: all of them or, by default, none. Build it, then pass it to
/// <see cref="IWorkspaceApi.ApplyAsync"/>.
/// </summary>
public sealed class WorkspaceEdit
{
    private readonly List<EditOperation> _operations = [];

    /// <summary>Shown in the status bar when the edit is applied, e.g. "Pulled 12 translations".</summary>
    public string? Label { get; set; }

    /// <summary>Refuse the whole edit when the project has changed since the snapshot with this revision.</summary>
    public long? BasedOnRevision { get; set; }

    /// <summary>Apply the steps that are fine and report the rest, instead of refusing everything when one is not.</summary>
    public bool AllowPartial { get; set; }

    public IReadOnlyList<EditOperation> Operations => _operations;

    public WorkspaceEdit Add(EditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(operation);
        return this;
    }

    public WorkspaceEdit SetValue(string key, string language, string value, string? expectedValue = null) => Add(new SetValue(key, language, value, expectedValue));
    public WorkspaceEdit SetComment(string key, string language, string comment) => Add(new SetComment(key, language, comment));
    public WorkspaceEdit SetReview(string key, string language, ReviewState state) => Add(new SetReview(key, language, state));
    public WorkspaceEdit AddKey(string key, IReadOnlyDictionary<string, string>? values = null) => Add(new AddKey(key, values));
    public WorkspaceEdit RenameKey(string key, string newKey) => Add(new RenameKey(key, newKey));
    public WorkspaceEdit DeleteKey(string key) => Add(new DeleteKey(key));
}

public enum EditIssueKind
{
    /// <summary>No project is open.</summary>
    NoProject,

    /// <summary>The project changed since the snapshot the edit was based on.</summary>
    Stale,
    UnknownKey,
    UnknownLanguage,
    InvalidKey,
    KeyExists,

    /// <summary>The translation no longer has the text the plugin expected.</summary>
    Conflict,

    /// <summary>The project's strict-approval policy refuses to approve a translation that has validation errors.</summary>
    ApprovalRefused,
    Empty,
}

/// <summary>A reason an edit, or one of its steps, was refused. <c>OperationIndex</c> is the step, or -1 for the edit as a whole.</summary>
public sealed record EditIssue(int OperationIndex, EditIssueKind Kind, string Message);

/// <summary>What validation found in a translation the edit changed. Findings never stop an edit, as they never stop a save.</summary>
public sealed record EditFinding(string Key, string? Language, string Severity, string Message);

/// <summary>
/// The outcome of an edit. <c>Applied</c> is false when it was refused, with the reasons in <c>Issues</c>; <c>ChangedUnits</c>
/// counts translations changed (values, comments, review states, keys added or removed); <c>Revision</c> is the project's now.
/// </summary>
public sealed record EditResult(bool Applied, int ChangedUnits, long Revision, IReadOnlyList<EditIssue> Issues, IReadOnlyList<EditFinding> Findings)
{
    public static EditResult Refused(long revision, params EditIssue[] issues) => new(false, 0, revision, issues, []);
}

/// <summary>
/// The controlled way for a plugin to read and change the open project. Edits go through the same paths as the user's: one
/// undo step for the values, dirty tracking, validation, the strict-approval policy, autosave and crash recovery. Nothing
/// of the host's models or view models is exposed.
/// </summary>
public interface IWorkspaceApi
{
    /// <summary>Whether a project is open right now.</summary>
    bool IsOpen { get; }

    /// <summary>A copy of the open project, or null when none is open. Includes what the user typed that has not been committed yet.</summary>
    Task<WorkspaceSnapshot?> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies the edit as one transaction. It never throws for a refused edit; read <see cref="EditResult.Issues"/>.</summary>
    Task<EditResult> ApplyAsync(WorkspaceEdit edit, CancellationToken cancellationToken = default);

    /// <summary>Raised after the project opens or closes or any edit (the user's or a plugin's) lands.</summary>
    event EventHandler? Changed;
}
