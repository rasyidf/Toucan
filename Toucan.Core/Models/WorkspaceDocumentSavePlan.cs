using System.Collections.ObjectModel;

namespace Toucan.Core.Models;

public sealed record SourceTextFormat(string EncodingName, bool HasBom)
{
    public static SourceTextFormat Utf8 { get; } = new("utf-8", false);
}

public sealed record WorkspaceSourceContent(string Content, string Version, SourceTextFormat TextFormat);

/// <summary>
/// Captured translation content; null binding means a new translation needs a destination.
/// Destination is a chosen workspace-relative document for a new or moved translation; null keeps the binding.
/// </summary>
public sealed record WorkspaceDocumentUnit(EntryId EntryId, string Language, TranslationPath Namespace,
    TranslationPath KeyPath, string Value, SourceBinding? Binding, string? RawScalar = null, WorkspaceEntryMetadata? Metadata = null,
    string? Destination = null);

/// <summary>Immutable content-only snapshot. Comments/review sidecars are planned separately before save execution.</summary>
public sealed class WorkspaceDocumentSnapshot(string root, Guid sessionId, long revision, IEnumerable<WorkspaceDocumentUnit> units)
{
    public string Root { get; } = root;
    public Guid SessionId { get; } = sessionId;
    public long Revision { get; } = revision;
    internal object? Capture { get; init; }
    public string? Manifest { get; init; }
    /// <summary>Source documents of languages removed from the workspace; saving deletes them.</summary>
    public IReadOnlyList<DocumentId> DeletedDocuments { get; init; } = [];
    public ReadOnlyCollection<WorkspaceDocumentUnit> Units { get; } = Array.AsReadOnly(units.ToArray());
}

public sealed record WorkspaceEntryDestination(EntryId EntryId, string Language, string RelativePath);

/// <summary>
/// Related documents sharing one framework layout family, e.g. every language's <c>checkout.json</c>.
/// Documents maps language to an existing or framework-described new document; languages the layout cannot place are absent.
/// </summary>
public sealed record WorkspaceDestinationFamily(string Id, string Label, TranslationPath Namespace,
    IReadOnlyDictionary<string, string> Documents, IReadOnlySet<string> ExistingDocuments);
public enum WorkspaceDocumentSaveOperation { Create, Update, Delete }

/// <summary>Null expected version requires a missing destination. Delete has null content.</summary>
public sealed record WorkspaceDocumentSaveTarget(DocumentId DocumentId, string RelativePath, string FormatId,
    WorkspaceDocumentSaveOperation Operation, string? ExpectedVersion, string? Content, SourceTextFormat TextFormat);

/// <summary>A pure document plan: no file access or mutation. It is not yet a complete workspace save (sidecars/checkpoints).</summary>
public sealed class WorkspaceDocumentSavePlan
{
    internal WorkspaceDocumentSavePlan(WorkspaceSource source, WorkspaceDocumentSnapshot snapshot,
        IEnumerable<WorkspaceDocumentSaveTarget> targets, IEnumerable<WorkspaceSourceUnit> units, IEnumerable<SourceDocument> documents, IEnumerable<WorkspaceEntryIdentity> identityCatalog, IEnumerable<WorkspaceArtifactSaveTarget>? artifacts = null)
    {
        Artifacts = Array.AsReadOnly(artifacts?.ToArray() ?? []);
        Source = source;
        Snapshot = snapshot;
        Root = source.Root;
        SessionId = snapshot.SessionId;
        CapturedRevision = snapshot.Revision;
        Documents = Array.AsReadOnly(documents.ToArray());
        IdentityCatalog = Array.AsReadOnly(identityCatalog.ToArray());
        Targets = Array.AsReadOnly(targets.ToArray());
        Units = Array.AsReadOnly(units.ToArray());
    }

    public ReadOnlyCollection<WorkspaceArtifactSaveTarget> Artifacts { get; }
    internal WorkspaceSource Source { get; }
    internal WorkspaceDocumentSnapshot Snapshot { get; }
    public ReadOnlyCollection<SourceDocument> Documents { get; }
    public ReadOnlyCollection<WorkspaceEntryIdentity> IdentityCatalog { get; }
    public string Root { get; }
    public Guid SessionId { get; }
    public long CapturedRevision { get; }
    public ReadOnlyCollection<WorkspaceDocumentSaveTarget> Targets { get; }
    public ReadOnlyCollection<WorkspaceSourceUnit> Units { get; }
}
