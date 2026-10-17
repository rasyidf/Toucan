using System.Collections.ObjectModel;

namespace Toucan.Core.Models;

public enum WorkspaceNavigationLayout { NamespaceFirst, FileFirst }
public enum WorkspaceNavigationNodeKind { Document, Namespace, KeyGroup, Entry }

/// <summary>Read-only presenter projection. Selection uses entry identities; labels and hierarchy never route writes.</summary>
public sealed class WorkspaceNavigationNode(string id, string label, WorkspaceNavigationNodeKind kind,
    IEnumerable<WorkspaceNavigationNode> children, IEnumerable<Guid> entryIds, IEnumerable<string> origins)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public WorkspaceNavigationNodeKind Kind { get; } = kind;
    public ReadOnlyCollection<WorkspaceNavigationNode> Children { get; } = Array.AsReadOnly(children.ToArray());
    public ReadOnlyCollection<Guid> EntryIds { get; } = Array.AsReadOnly(entryIds.Distinct().ToArray());
    public ReadOnlyCollection<string> Origins { get; } = Array.AsReadOnly(origins.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}

public sealed class WorkspaceNavigationProjection(Guid sessionId, long revision, WorkspaceNavigationLayout layout, IEnumerable<WorkspaceNavigationNode> roots)
{
    public Guid SessionId { get; } = sessionId;
    public long Revision { get; } = revision;
    public WorkspaceNavigationLayout Layout { get; } = layout;
    public ReadOnlyCollection<WorkspaceNavigationNode> Roots { get; } = Array.AsReadOnly(roots.ToArray());
    public IReadOnlyList<Guid> RetainSelection(IEnumerable<Guid> selected)
    {
        var present = Roots.SelectMany(n => n.EntryIds).ToHashSet();
        return selected.Where(present.Contains).Distinct().ToArray();
    }
}
