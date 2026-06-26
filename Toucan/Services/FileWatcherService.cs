using System;
using System.IO;

namespace Toucan.Services;

/// <summary>
/// Watches the project folder for file changes and raises an event.
/// ponytail: simple FileSystemWatcher wrapper. Debounces rapid changes.
/// </summary>
internal class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private bool _pending;

    public event Action? FilesChanged;

    public void Watch(string folder)
    {
        Stop();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;

        _debounce = new System.Timers.Timer(2000) { AutoReset = false };
        _debounce.Elapsed += (_, _) =>
        {
            _pending = false;
            FilesChanged?.Invoke();
        };
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        // Skip temp/build files
        if (e.FullPath.Contains(".obj") || e.FullPath.Contains(".bin")) return;
        if (!_pending)
        {
            _pending = true;
            _debounce?.Stop();
            _debounce?.Start();
        }
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
