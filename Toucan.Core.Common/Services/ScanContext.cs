using System.Diagnostics;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Ambient cancellation + progress for folder scans. <see cref="FileEnumerator"/> is the single choke point
/// every load strategy and the framework detector walk through, so flowing the context ambiently gives all
/// of them cancellation and progress without threading parameters through every strategy.
/// </summary>
public sealed class ScanContext
{
    private static readonly AsyncLocal<ScanContext?> s_current = new();
    private static readonly long s_minReportTicks = Stopwatch.Frequency / 10; // ~10 reports/sec keeps the UI thread calm

    private readonly CancellationToken _ct;
    private readonly IProgress<ScanProgress>? _progress;
    private int _dirs;
    private int _files;
    private long _lastReport;

    private ScanContext(CancellationToken ct, IProgress<ScanProgress>? progress)
    {
        _ct = ct;
        _progress = progress;
    }

    public static ScanContext? Current => s_current.Value;

    /// <summary>Makes the given token/progress ambient until the returned scope is disposed.</summary>
    public static IDisposable Begin(CancellationToken ct, IProgress<ScanProgress>? progress)
    {
        var previous = s_current.Value;
        s_current.Value = new ScanContext(ct, progress);
        return new Scope(previous);
    }

    internal void OnDirectory(string dir)
    {
        _ct.ThrowIfCancellationRequested();
        _dirs++;
        Report(dir);
    }

    internal void OnFile()
    {
        _ct.ThrowIfCancellationRequested();
        _files++;
    }

    private void Report(string dir)
    {
        if (_progress == null) return;
        var now = Stopwatch.GetTimestamp();
        if (now - _lastReport < s_minReportTicks) return;
        _lastReport = now;
        _progress.Report(new ScanProgress(_dirs, _files, dir));
    }

    private sealed class Scope(ScanContext? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
