using System.Security.Cryptography;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>
/// Conditional document mutations through an <see cref="IDocumentSourceSession"/>. Callers see the same versions as the
/// local store (SHA-256 of the bytes, null for a missing file), so <c>WorkspaceDocumentSavePlanner</c> and its executor
/// work unchanged. The service's own opaque version is used underneath: a write first reads the document, checks that its
/// content is the one the caller expected, then sends the conditional write with the service's version, so a change
/// between the check and the write still conflicts at the service.
/// </summary>
public sealed class RemoteWorkspaceDocumentStore(IDocumentSourceSession session, SourceCapabilities capabilities = RemoteWorkspaceDocumentStore.ReadWrite) : IWorkspaceDocumentStore
{
    public const SourceCapabilities ReadWrite = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments;

    /// <summary>
    /// Reading needs <c>ReadDocuments</c> and changing needs <c>WriteDocuments</c>. Pass the adapter's capabilities narrowed by
    /// the connection's permissions: an operation outside them is refused here, before anything reaches the service.
    /// </summary>
    public SourceCapabilities Capabilities { get; } = capabilities;

    internal static void Require(SourceCapabilities have, SourceCapabilities need, string what)
    {
        if ((have & need) != need)
            throw new SourceException(new SourceError(SourceErrorKind.Unsupported, $"This source cannot {what}."));
    }

    public async Task<string?> VersionAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        Require(Capabilities, SourceCapabilities.ReadDocuments, "read documents");
        return (await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false))?.ContentVersion;
    }

    public async Task WriteAsync(string root, string relativePath, byte[] bytes, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Require(Capabilities, ReadWrite, "write documents");
        var path = RemotePath(relativePath);
        string? remoteVersion = null;
        if (expectedVersion is not null)
        {
            var current = await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (current?.ContentVersion != expectedVersion)
                throw new WorkspaceSourceConflictException(relativePath, expectedVersion, current?.ContentVersion);
            remoteVersion = current.RemoteVersion;
        }
        var result = await session.WriteDocumentAsync(new(Guid.NewGuid(), new(path), bytes, remoteVersion), cancellationToken).ConfigureAwait(false);
        Check(result, relativePath, expectedVersion);
    }

    public async Task DeleteAsync(string root, string relativePath, string expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedVersion);
        Require(Capabilities, ReadWrite, "delete documents");
        var path = RemotePath(relativePath);
        var current = await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (current?.ContentVersion != expectedVersion)
            throw new WorkspaceSourceConflictException(relativePath, expectedVersion, current?.ContentVersion);
        var result = await session.DeleteDocumentAsync(new(path), current.RemoteVersion, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        Check(result, relativePath, expectedVersion);
    }

    private sealed record Snapshot(string ContentVersion, string RemoteVersion);

    private async Task<Snapshot?> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await session.ReadDocumentAsync(new(RemotePath(relativePath)), cancellationToken).ConfigureAwait(false);
            return new(Convert.ToHexString(SHA256.HashData(content.Bytes.Span)), content.Version);
        }
        catch (SourceException ex) when (ex.Error.Kind == SourceErrorKind.NotFound)
        {
            return null;
        }
    }

    private static void Check(DocumentWriteResult result, string relativePath, string? expectedVersion)
    {
        switch (result.Outcome)
        {
            case UnitOperationOutcome.Applied or UnitOperationOutcome.AlreadyApplied:
                return;
            case UnitOperationOutcome.Pending:
                throw new SourceException(new SourceError(SourceErrorKind.Unsupported,
                    $"The source finishes writing '{relativePath}' later; asynchronous document writes are handled by the sync engine."));
            case UnitOperationOutcome.Conflict:
                throw new WorkspaceSourceConflictException(relativePath, expectedVersion, null);
            case UnitOperationOutcome.NotFound when expectedVersion is not null:
                throw new WorkspaceSourceConflictException(relativePath, expectedVersion, null);
            default:
                // Unknown and None may have reached the service; the error type tells the save not to assume either way.
                throw new SourceException(result.Error ?? new SourceError(
                    result.Outcome is UnitOperationOutcome.Unknown or UnitOperationOutcome.None ? SourceErrorKind.Unknown : SourceErrorKind.Internal,
                    $"The source did not apply '{relativePath}' ({result.Outcome})."));
        }
    }

    /// <summary>Document paths on a service use '/' and are relative to the connection.</summary>
    internal static string RemotePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        if (path.Split('/').Any(s => s == ".."))
            throw new InvalidOperationException("Destination is outside the workspace.");
        return path;
    }
}
