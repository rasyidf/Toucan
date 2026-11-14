namespace Toucan.Core.Models;

/// <summary>Content versions use the same byte fingerprint as document persistence; null means absent.</summary>
public sealed record FileVersionChange(string RelativePath, string? PreviousVersion, string? CurrentVersion);

public sealed class FileWatcherChangeEventArgs(string root, IReadOnlyList<FileVersionChange> changes, long generation = 0) : EventArgs
{
    public string Root { get; } = root;
    public long Generation { get; } = generation;
    public IReadOnlyList<FileVersionChange> Changes { get; } = Array.AsReadOnly(changes.ToArray());
}
