namespace Toucan.Plugins.Sources;

/// <summary>
/// A session for a service that stores whole files (Crowdin or Transifex file resources, a Git host, S3, WebDAV). The
/// adapter only moves bytes with versions; the host parses and writes them with Toucan's format strategies, so remote files
/// get the same minimal, byte-exact saves as local ones.
/// </summary>
public interface IDocumentSourceSession : IContentSourceSession
{
    /// <summary>Lists documents page by page; the last page sets <see cref="RemoteDocumentPage.IsComplete"/>.</summary>
    IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one document. Throws <see cref="SourceException"/> (NotFound) when it does not exist.</summary>
    Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken);

    /// <summary>Writes when the document's version is still <see cref="RemoteDocumentWrite.ExpectedVersion"/> (null: must not exist).</summary>
    Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken);

    /// <summary>Deletes when the document's version is still <paramref name="expectedVersion"/>.</summary>
    Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken);
}

/// <param name="Prefix">Only paths under this one; null lists everything in the connection.</param>
public sealed record DocumentListRequest(string? Prefix = null, string? ContinuationToken = null);

/// <param name="Path">Relative, with '/' separators.</param>
public sealed record RemoteDocumentRef(string Path, string? Language = null, string? ResourceId = null);

public sealed record RemoteDocumentPage(IReadOnlyList<RemoteDocumentInfo> Documents, string? ContinuationToken, bool IsComplete);

/// <param name="Version">Opaque version (ETag, content hash, commit), compared only for equality.</param>
public sealed record RemoteDocumentInfo(RemoteDocumentRef Ref, string Version, long? Length = null);

public sealed record RemoteDocumentContent(RemoteDocumentRef Ref, ReadOnlyMemory<byte> Bytes, string Version);

/// <param name="FormatId">The Toucan format the bytes are in, for services that convert files.</param>
public sealed record RemoteDocumentWrite(Guid OperationId, RemoteDocumentRef Ref, ReadOnlyMemory<byte> Bytes, string? ExpectedVersion, string? FormatId = null);

public sealed record DocumentWriteResult(UnitOperationOutcome Outcome, string? NewVersion = null, SourceError? Error = null, SourceJobHandle? Job = null);
