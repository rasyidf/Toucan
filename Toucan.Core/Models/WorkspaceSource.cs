using System.Collections.ObjectModel;

namespace Toucan.Core.Models;

public readonly record struct DocumentId(Guid Value);
public readonly record struct EntryId(Guid Value);

/// <summary>
/// Identity checkpoint for reopen/recovery. After a rename or move, update the address and retain Id.
/// The persistence layer must save this catalog with the corresponding documents.
/// </summary>
public sealed record WorkspaceEntryIdentity(string Resource, TranslationPath Namespace, TranslationPath KeyPath, EntryId Id);

/// <summary>An identity catalog belongs to one source root, not to a presenter or application scope.</summary>
public sealed record WorkspaceIdentityCheckpoint(string Root, IReadOnlyList<WorkspaceEntryIdentity> Entries);

/// <summary>Document origin and exact version read. Content retains constructs not exposed as editable units.</summary>
public sealed record SourceDocument(DocumentId Id, string RelativePath, string Language, string? Package,
    string FormatId, string Version, string Content)
{
    public SourceTextFormat TextFormat { get; init; } = SourceTextFormat.Utf8;
}

public sealed record SourceBinding(DocumentId DocumentId, TranslationPath Location);

/// <summary>Immutable unit. Rename changes its address, not its EntryId or document ownership.</summary>
public sealed record WorkspaceSourceUnit(EntryId EntryId, string Language, TranslationPath Namespace,
    TranslationPath KeyPath, string Value, SourceBinding Binding, string? RawScalar = null, WorkspaceEntryMetadata? Metadata = null);

/// <summary>Source snapshot, independent of UI state, plugin activation, or a save strategy.</summary>
public sealed class WorkspaceSource
{
    public WorkspaceSource(string root, string frameworkId, string formatId,
        IEnumerable<SourceDocument> documents, IEnumerable<WorkspaceSourceUnit> units,
        IEnumerable<WorkspaceEntryIdentity>? identityCatalog = null, IEnumerable<WorkspaceSourceArtifact>? artifacts = null)
    {
        Artifacts = Array.AsReadOnly(artifacts?.ToArray() ?? []);
        Root = root;
        FrameworkId = frameworkId;
        FormatId = formatId;
        Documents = Array.AsReadOnly(documents.ToArray());
        Units = Array.AsReadOnly(units.ToArray());
        IdentityCatalog = Array.AsReadOnly(identityCatalog?.ToArray() ?? []);
    }

    public ReadOnlyCollection<WorkspaceSourceArtifact> Artifacts { get; }
    public string Root { get; }
    public string FrameworkId { get; }
    public string FormatId { get; }
    public ReadOnlyCollection<SourceDocument> Documents { get; }
    public ReadOnlyCollection<WorkspaceSourceUnit> Units { get; }
    public ReadOnlyCollection<WorkspaceEntryIdentity> IdentityCatalog { get; }
}
