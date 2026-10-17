using System.Security.Cryptography;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Optimistic local writes. Checks versions immediately before replacement; external writers must still cooperate.</summary>
public sealed class LocalWorkspaceDocumentStore : IWorkspaceDocumentStore
{
    public async Task<string?> VersionAsync(string root, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = SafePath(root, relativePath);
        try { return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task WriteAsync(string root, string relativePath, byte[] bytes, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        var path = SafePath(root, relativePath);
        return AtomicFile.WriteAllBytesAsync(path, bytes, () =>
        {
            SafePath(root, relativePath);
            CheckVersion(path, relativePath, expectedVersion);
        }, requireExisting: expectedVersion is not null, cancellationToken);
    }

    public Task DeleteAsync(string root, string relativePath, string expectedVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = SafePath(root, relativePath);
        CheckVersion(path, relativePath, expectedVersion);
        File.Delete(path);
        return Task.CompletedTask;
    }

    internal static string? Version(string path)
    {
        try { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void CheckVersion(string path, string relativePath, string? expected)
    {
        var actual = Version(path);
        if (actual != expected) throw new WorkspaceSourceConflictException(relativePath, expected, actual);
    }

    internal static string SafePath(string root, string relativePath)
    {
        root = Path.GetFullPath(root);
        var path = WorkspaceDocumentRouter.WithinRoot(root, relativePath);
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        RejectLink(current);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectLink(current);
        }
        return path;
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
            throw new IOException($"Symbolic links are not supported in document save targets: '{path}'.");
    }
}
