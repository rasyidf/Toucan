using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Monitors the project folder for external file changes and provides snapshot-based
/// change detection to prevent false notifications after save operations.
/// </summary>
public interface IFileWatcherService
{
    /// <summary>Starts watching a folder recursively for file changes.</summary>
    void Watch(string folder);

    /// <summary>Stops watching and releases file system resources.</summary>
#pragma warning disable CA1716 // Identifiers should not match keywords — Stop is the natural verb for watcher control
    void Stop();
#pragma warning restore CA1716

    /// <summary>Records the current folder contents as the baseline snapshot.</summary>
    void TakeSnapshot();

    /// <summary>Changes when a watch is stopped or replaced; zero is the legacy implementation default.</summary>
    long WatchGeneration => 0;

    /// <summary>Reports paths and content versions that differ from the acknowledged baseline.</summary>
    IReadOnlyList<FileVersionChange> GetChanges() => [];

    /// <summary>Acknowledges only committed targets; implementations must not resnapshot unrelated files.</summary>
    void AcknowledgeWrites(string root, IReadOnlyList<FileVersionChange> writes) { }

    /// <summary>Raised when external file changes are detected (after debounce).</summary>
    event EventHandler? FilesChanged;
}
