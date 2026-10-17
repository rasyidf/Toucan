using Toucan.Core.Models;

namespace Toucan.Plugins;

/// <summary>One immutable translation addressed by stable entry identity and language.</summary>
public sealed record IdentifiedUnitSnapshot(Guid EntryId, string Language, string Key, TranslationPath Namespace,
    TranslationPath KeyPath, string Value, string Comment, ReviewState Review, bool IsModified,
    Guid? DocumentId = null, string? SourceLocation = null);

public sealed record IdentifiedWorkspaceSnapshot(string WorkspaceId, Guid SessionId, string ProjectPath,
    string PrimaryLanguage, IReadOnlyList<string> Languages, IReadOnlyList<IdentifiedUnitSnapshot> Units, long Revision)
{
    public bool IsClosing { get; init; }
}

public abstract record EntryOperation(Guid EntryId);
public sealed record SetEntryValue(Guid EntryId, string Language, string Value, string? ExpectedValue = null) : EntryOperation(EntryId);
public sealed record SetEntryComment(Guid EntryId, string Language, string Comment) : EntryOperation(EntryId);
public sealed record SetEntryReview(Guid EntryId, string Language, ReviewState State) : EntryOperation(EntryId);
public sealed record RenameEntry(Guid EntryId, TranslationPath Namespace, TranslationPath KeyPath) : EntryOperation(EntryId);
public sealed record DeleteEntry(Guid EntryId) : EntryOperation(EntryId);

/// <summary>All operations are accepted together. SessionId protects work prepared before a close/reopen.</summary>
public sealed record EntryEdit(Guid SessionId, long BasedOnRevision, IReadOnlyList<EntryOperation> Operations, string? Label = null);

/// <summary>Optional API 1.2 extension; legacy key addressing remains available through IWorkspaceApi.</summary>
public interface IIdentifiedWorkspaceApi : IWorkspaceApi
{
    Task<IdentifiedWorkspaceSnapshot?> SnapshotEntriesAsync(CancellationToken cancellationToken = default);
    Task<EditResult> ApplyEntriesAsync(EntryEdit edit, CancellationToken cancellationToken = default);
}
