using Toucan.Core.Contracts.Services;

namespace Toucan.Avalonia.Services;

/// <summary>
/// Watches the project folder for external edits. Debounces bursts of events and only raises
/// <see cref="FilesChanged"/> when file timestamps differ from the last snapshot, so Toucan's
/// own saves don't trigger a reload prompt.
/// </summary>
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
    private readonly Dictionary<string, DateTime> _snapshots = [];
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private string _folder = string.Empty;

    public event EventHandler? FilesChanged;

    public void Watch(string folder)
    {
        Stop();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        _folder = folder;
        TakeSnapshot();

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;

        _debounce = new System.Timers.Timer(2000) { AutoReset = false };
        _debounce.Elapsed += (_, _) =>
        {
            if (HasChanges()) FilesChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void TakeSnapshot()
    {
        lock (_gate)
        {
            _snapshots.Clear();
            foreach (var f in EnumerateFiles())
                _snapshots[f] = File.GetLastWriteTimeUtc(f);
        }
    }

    private bool HasChanges()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return true;
        lock (_gate)
        {
            var seen = 0;
            foreach (var f in EnumerateFiles())
            {
                seen++;
                if (!_snapshots.TryGetValue(f, out var prev) || File.GetLastWriteTimeUtc(f) != prev) return true;
            }
            return seen != _snapshots.Count;
        }
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return [];
        try
        {
            return Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Where(f => !IsIgnored(f)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsIgnored(string path) =>
        IgnoredSegments.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase))
        || Path.GetFileName(path).Equals(".DS_Store", StringComparison.Ordinal);

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        if (IsIgnored(e.FullPath)) return;
        _debounce?.Stop();
        _debounce?.Start();
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Dispose();
        _debounce = null;
    }

    public void Dispose() => Stop();
}
