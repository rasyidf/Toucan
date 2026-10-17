using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Local source reads. Constructing/registering this service performs no I/O.</summary>
public sealed class LocalWorkspaceSourceReader : IWorkspaceSourceReader
{
    public async Task<WorkspaceSourceContent> ReadAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = WorkspaceDocumentRouter.WithinRoot(root, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var encoding = reader.CurrentEncoding;
        var preamble = encoding.GetPreamble();
        return new(content, Convert.ToHexString(SHA256.HashData(bytes)),
            new(encoding.WebName, preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble)));
    }
}
