using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services;

/// <summary>Pure multilevel navigator for any presenter. Document ownership is supplied by the source, never inferred from labels.</summary>
public sealed class WorkspaceNavigator
{
    private sealed class Node(string id, string label, WorkspaceNavigationNodeKind kind)
    {
        public string Id { get; } = id;
        public string Label { get; } = label;
        public WorkspaceNavigationNodeKind Kind { get; } = kind;
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public HashSet<Guid> Entries { get; } = [];
        public HashSet<string> Origins { get; } = new(StringComparer.Ordinal);
        public WorkspaceNavigationNode Freeze() => new(Id, Label, Kind, Children.Values.OrderBy(n => n.Label, StringComparer.Ordinal)
            .ThenBy(n => n.Id, StringComparer.Ordinal).Select(n => n.Freeze()), Entries, Origins);
    }

    public WorkspaceNavigationProjection Build(IdentifiedWorkspaceSnapshot snapshot, WorkspaceSource? source, WorkspaceNavigationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (source is not null && Path.GetFullPath(source.Root) != Path.GetFullPath(snapshot.ProjectPath))
            throw new InvalidOperationException("Navigation source belongs to another workspace.");
        var documents = source?.Documents.ToDictionary(d => d.Id.Value) ?? [];
        var root = new Node("root", string.Empty, WorkspaceNavigationNodeKind.Namespace);
        foreach (var unit in snapshot.Units)
        {
            var origin = unit.DocumentId is { } documentId && documents.TryGetValue(documentId, out var doc) ? doc.RelativePath : "Unassigned";
            var node = root;
            AddUnit(node);
            if (layout == WorkspaceNavigationLayout.FileFirst)
                node = Child(node, WorkspaceNavigationNodeKind.Document, unit.DocumentId?.ToString() ?? "unassigned", origin);
            foreach (var segment in unit.Namespace.Segments)
                node = Child(node, WorkspaceNavigationNodeKind.Namespace, segment, segment);
            foreach (var segment in unit.KeyPath.Segments.Take(Math.Max(0, unit.KeyPath.Segments.Count - 1)))
                node = Child(node, WorkspaceNavigationNodeKind.KeyGroup, segment, segment);
            Child(node, WorkspaceNavigationNodeKind.Entry, unit.EntryId.ToString(), unit.KeyPath.Segments.LastOrDefault() ?? unit.Key);

            Node Child(Node parent, WorkspaceNavigationNodeKind kind, string key, string label)
            {
                var id = JsonSerializer.Serialize(new[] { parent.Id, kind.ToString(), key });
                if (!parent.Children.TryGetValue(id, out var child)) parent.Children.Add(id, child = new(id, label, kind));
                AddUnit(child);
                return child;
            }
            void AddUnit(Node target) { target.Entries.Add(unit.EntryId); target.Origins.Add(origin); }
        }
        return new(snapshot.SessionId, snapshot.Revision, layout, root.Children.Values.OrderBy(n => n.Label, StringComparer.Ordinal).Select(n => n.Freeze()));
    }
}
