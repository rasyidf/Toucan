using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>Source access independent of discovery/layout and document serialization. Implementations own no workspace lifecycle.</summary>
public interface IWorkspaceSourceReader
{
    Task<WorkspaceSourceContent> ReadAsync(string root, string relativePath, CancellationToken cancellationToken = default);
    async Task<WorkspaceSourceContent?> ReadOptionalAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        try { return await ReadAsync(root, relativePath, cancellationToken).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}

