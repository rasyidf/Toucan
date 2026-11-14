using System.Security.Cryptography;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>Debounces file events and compares byte fingerprints with acknowledged source versions.</summary>
public sealed class FileWatcherService : IFileWatcherService, IDisposable
{
    private static readonly string[] IgnoredSegments =
    [
        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        ".obj", ".bin"
    ];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _snapshots = new(StringComparer.Ordinal);
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private string _folder = string.Empty;
    private long _generation;

    public event EventHandler? FilesChanged;
    public long WatchGeneration { get { lock (_gate) return _generation; } }

    public void Watch(string folder)
    {
        Stop();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        lock (_gate)
        {
            _folder = Path.GetFullPath(folder);
            TakeSnapshot();
            var generation = _generation;
            _debounce = new System.Timers.Timer(2000) { AutoReset = false };
            _debounce.Elapsed += (_, _) => PublishChanges(generation);
            _watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            _watcher.Changed += OnChange;
            _watcher.Created += OnChange;
            _watcher.Deleted += OnChange;
            _watcher.Renamed += OnChange;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void TakeSnapshot()
    {
        lock (_gate)
        {
            var current = ReadVersions();
            _snapshots.Clear();
            foreach (var (path, version) in current) _snapshots[path] = version;
        }
    }

    public IReadOnlyList<FileVersionChange> GetChanges()
    {
        lock (_gate)
        {
            var current = ReadVersions();
            return _snapshots.Keys.Union(current.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(path => new FileVersionChange(path, _snapshots.GetValueOrDefault(path), current.GetValueOrDefault(path)))
                .Where(change => change.PreviousVersion != change.CurrentVersion).ToArray();
        }
    }

    public void AcknowledgeWrites(string root, IReadOnlyList<FileVersionChange> writes)
    {
        lock (_gate)
        {
            if (!string.Equals(_folder, Path.GetFullPath(root), StringComparison.Ordinal)) return;
            foreach (var write in writes)
            {
                if (write.CurrentVersion is null) _snapshots.Remove(write.RelativePath);
                else _snapshots[write.RelativePath] = write.CurrentVersion;
            }
        }
    }

    private Dictionary<string, string> ReadVersions()
    {
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return current;
        foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Where(f => !IsIgnored(f)))
        {
            try
            {
                using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                current[Path.GetRelativePath(_folder, file).Replace('\\', '/')] = Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return current;
    }

    private static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        var metadata = path.Contains($"{Path.DirectorySeparatorChar}.toucan{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        var ownedMetadata = name == "workspace-identities.json" || path.Contains($"{Path.DirectorySeparatorChar}.toucan{Path.DirectorySeparatorChar}entries{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        return (metadata && !ownedMetadata)
            || IgnoredSegments.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase))
            || name.Equals(".DS_Store", StringComparison.Ordinal)
            || (name.StartsWith('.') && path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private void PublishChanges(long generation)
    {
        FileWatcherChangeEventArgs notification;
        lock (_gate)
        {
            if (generation != _generation || _watcher is null) return;
            try
            {
                var changes = GetChanges();
                if (changes.Count == 0) return;
                notification = new(_folder, changes, generation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        }
        FilesChanged?.Invoke(this, notification);
    }

    internal void RaiseFilesChanged(FileWatcherChangeEventArgs? args = null) =>
        FilesChanged?.Invoke(this, args ?? new FileWatcherChangeEventArgs(_folder, GetChanges(), WatchGeneration));

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        if (IsIgnored(e.FullPath) && (e is not RenamedEventArgs renamed || IsIgnored(renamed.OldFullPath))) return;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _watcher)) return;
            _debounce?.Stop();
            _debounce?.Start();
        }
    }

    public void Stop()
    {
        FileSystemWatcher? watcher;
        System.Timers.Timer? debounce;
        lock (_gate)
        {
            _generation++;
            watcher = _watcher; _watcher = null;
            debounce = _debounce; _debounce = null;
            _folder = string.Empty;
            _snapshots.Clear();
        }
        // Native watcher disposal can wait for callbacks, which also acquire the gate.
        watcher?.Dispose();
        debounce?.Dispose();
    }

    public void Dispose() => Stop();
}
