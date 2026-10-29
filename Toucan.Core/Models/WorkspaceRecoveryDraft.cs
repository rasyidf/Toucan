using System.Collections.ObjectModel;

namespace Toucan.Core.Models;

/// <summary>Identity-preserving, root-bound recovery. The saved baseline detects external edits before replay.</summary>
public sealed class WorkspaceRecoveryDraft(string root, string frameworkId, string formatId,
    IReadOnlyList<WorkspaceDocumentUnit> baseline, IReadOnlyList<WorkspaceDocumentUnit> units, WorkspaceIdentityCheckpoint identityCheckpoint)
{
    public string Root { get; } = root;
    public string FrameworkId { get; } = frameworkId;
    public string FormatId { get; } = formatId;
    public IReadOnlyList<WorkspaceDocumentUnit> Baseline { get; } = Array.AsReadOnly(baseline.ToArray());
    public IReadOnlyList<WorkspaceDocumentUnit> Units { get; } = Array.AsReadOnly(units.ToArray());
    public WorkspaceIdentityCheckpoint IdentityCheckpoint { get; } = new(identityCheckpoint.Root, Array.AsReadOnly(identityCheckpoint.Entries.ToArray()));
    /// <summary>Languages removed but not yet saved; their documents are deleted by the next save. Older drafts omit it.</summary>
    public IReadOnlyList<string> RemovedLanguages { get; init; } = [];
    public int ChangeCount
    {
        get
        {
            var before = Baseline.ToDictionary(u => (u.EntryId, u.Language));
            var after = Units.ToDictionary(u => (u.EntryId, u.Language));
            return before.Keys.Union(after.Keys).Count(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k));
        }
    }
}

public sealed record WorkspaceRecoveryResult(int Applied, IReadOnlyList<EntryId> Conflicts);
