namespace Toucan.Core.Models;

/// <summary>Snapshot of a folder scan in flight, reported while a project is loading.</summary>
public readonly record struct ScanProgress(int DirectoriesScanned, int FilesFound, string? CurrentDirectory);
