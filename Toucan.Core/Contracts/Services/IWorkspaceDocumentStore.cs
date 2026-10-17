namespace Toucan.Core.Contracts.Services;

/// <summary>Conditional local document mutations. Versions are SHA-256 bytes; null denotes a missing file.</summary>
public interface IWorkspaceDocumentStore
{
    Task<string?> VersionAsync(string root, string relativePath, CancellationToken cancellationToken = default);
    Task WriteAsync(string root, string relativePath, byte[] bytes, string? expectedVersion, CancellationToken cancellationToken = default);
    Task DeleteAsync(string root, string relativePath, string expectedVersion, CancellationToken cancellationToken = default);
}
