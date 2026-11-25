using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>
/// Reads workspace files through an <see cref="IDocumentSourceSession"/>, so <c>WorkspaceSourceLoader</c> and the format
/// strategies work over a remote store exactly as they do over a folder. The <c>root</c> argument names the connection and
/// is otherwise ignored. Versions are the SHA-256 of the bytes, like the local reader's, so a loaded document's version
/// stays comparable to what <see cref="RemoteWorkspaceDocumentStore"/> reports whatever opaque version the service uses.
/// </summary>
public sealed class RemoteWorkspaceSourceReader(IDocumentSourceSession session, SourceCapabilities capabilities = SourceCapabilities.ReadDocuments) : IWorkspaceSourceReader
{
    public async Task<WorkspaceSourceContent> ReadAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        RemoteWorkspaceDocumentStore.Require(capabilities, SourceCapabilities.ReadDocuments, "read documents");
        RemoteDocumentContent remote;
        try
        {
            remote = await session.ReadDocumentAsync(new(RemoteWorkspaceDocumentStore.RemotePath(relativePath)), cancellationToken).ConfigureAwait(false);
        }
        catch (SourceException ex) when (ex.Error.Kind == SourceErrorKind.NotFound)
        {
            // Matches the local reader, so ReadOptionalAsync treats a missing remote file as absent.
            throw new FileNotFoundException($"'{relativePath}' does not exist in the source.", relativePath, ex);
        }
        var bytes = remote.Bytes.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var encoding = reader.CurrentEncoding;
        var preamble = encoding.GetPreamble();
        return new(content, Convert.ToHexString(SHA256.HashData(bytes)),
            new(encoding.WebName, preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)));
    }
}
