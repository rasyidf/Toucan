using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Save As/export is a create-only destination plan. It never mutates or acknowledges the source workspace.</summary>
public sealed class WorkspaceExportPlanner(WorkspaceDocumentSavePlanner documents)
{
    public WorkspaceDocumentSavePlan Plan(WorkspaceSource source, WorkspaceDocumentSnapshot snapshot, string destination, CancellationToken cancellationToken = default)
    {
        destination = Path.GetFullPath(destination);
        var relative = Path.GetRelativePath(source.Root, destination);
        if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Export destination must be outside the source workspace.");
        var plan = documents.Plan(source, snapshot, cancellationToken: cancellationToken);
        var targets = plan.Documents.Select(d => new WorkspaceDocumentSaveTarget(d.Id, d.RelativePath, d.FormatId, WorkspaceDocumentSaveOperation.Create, null,
            plan.Targets.SingleOrDefault(t => t.DocumentId == d.Id)?.Content ?? d.Content, d.TextFormat)).ToArray();
        var artifacts = source.Artifacts.ToDictionary(a => a.RelativePath, a => new WorkspaceArtifactSaveTarget(a.RelativePath,
            WorkspaceDocumentSaveOperation.Create, null, a.Content, a.TextFormat), StringComparer.Ordinal);
        foreach (var artifact in plan.Artifacts)
            if (artifact.Operation == WorkspaceDocumentSaveOperation.Delete) artifacts.Remove(artifact.RelativePath);
            else artifacts[artifact.RelativePath] = artifact with { Operation = WorkspaceDocumentSaveOperation.Create, ExpectedVersion = null };
        var empty = new WorkspaceSource(destination, source.FrameworkId, source.FormatId, [], []);
        return new(empty, new(destination, snapshot.SessionId, snapshot.Revision, snapshot.Units), targets, plan.Units, plan.Documents, plan.IdentityCatalog, artifacts.Values);
    }
}
