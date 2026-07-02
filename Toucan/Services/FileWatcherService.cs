using System;
using System.Collections.Generic;
using System.IO;
using Toucan.Core.Contracts.Services;

namespace Toucan.Services;

/// <summary>
/// Watches the project folder for file changes and raises an event.
/// ponytail: simple FileSystemWatcher wrapper. Debounces rapid changes.
/// Only fires FilesChanged if timestamps actually differ from snapshot.
/// </summary>
public class FileWatcherService : IFileWatcherService, IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private int _pending; // 0 = idle, 1 = pending; use Interlocked for thread-safety
    private string _folder = "";
    private readonly Dictionary<string, DateTime> _snapshots = [];

    public event EventHandler? FilesChanged;

    public void Watch(string folder)
    {
        Stop();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

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

        _debounce = new System.Timers.Timer(2000) { AutoReset = false };
        _debounce.Elapsed += (_, _) =>
        {
            System.Threading.Interlocked.Exchange(ref _pending, 0);
            if (HasChanges())
            {
                FilesChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>Record current file timestamps. Call after a successful load/save.</summary>
    public void TakeSnapshot()
    {
        _snapshots.Clear();
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
        {
            return;
        }

        foreach (string f in Directory.GetFiles(_folder, "*.*", SearchOption.AllDirectories))
        {
            if (f.Contains(".obj") || f.Contains(".bin"))
            {
                continue;
            }

            _snapshots[f] = File.GetLastWriteTimeUtc(f);
        }
    }

    private bool HasChanges()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
        {
            return true;
        }

        foreach (string f in Directory.GetFiles(_folder, "*.*", SearchOption.AllDirectories))
        {
            if (f.Contains(".obj") || f.Contains(".bin"))
            {
                continue;
            }

            var lwt = File.GetLastWriteTimeUtc(f);
            if (!_snapshots.TryGetValue(f, out var prev) || lwt != prev)
            {
                return true;
            }
        }
        return false;
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains(".obj") || e.FullPath.Contains(".bin"))
        {
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _pending, 1, 0) == 0)
        {
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

    public void Dispose()
    {
        Stop();
    }
}
