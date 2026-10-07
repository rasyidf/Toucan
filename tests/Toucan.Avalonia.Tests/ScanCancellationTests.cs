using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class ScanCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-scan-" + Guid.NewGuid().ToString("N"));

    public ScanCancellationTests()
    {
        for (int i = 0; i < 20; i++)
        {
            var dir = Path.Combine(_root, $"d{i}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "a.json"), "{}");
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void EnumerateFiles_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var _ = ScanContext.Begin(cts.Token, null);

        Assert.Throws<OperationCanceledException>(() => FileEnumerator.EnumerateFiles(_root, "*.json").ToList());
    }

    [Fact]
    public void EnumerateFiles_CancelMidScan_StopsEarly()
    {
        using var cts = new CancellationTokenSource();
        using var _ = ScanContext.Begin(cts.Token, null);
        int seen = 0;

        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (var _ in FileEnumerator.EnumerateFiles(_root, "*.json"))
            {
                if (++seen == 3) cts.Cancel();
            }
        });
        Assert.Equal(3, seen);
    }

    [Fact]
    public void EnumerateFiles_ReportsProgress()
    {
        ScanProgress? last = null;
        using var _ = ScanContext.Begin(default, new SyncProgress(p => last = p));

        var files = FileEnumerator.EnumerateFiles(_root, "*.json").ToList();
        Assert.Equal(20, files.Count);

        Assert.NotNull(last); // first directory always reports (throttle starts open)
    }

    [Fact]
    public void FormatDetector_DetectsAndHonoursCancel()
    {
        using var host = new TestHost();
        var detector = host.Services.GetRequiredService<FormatDetector>();
        File.WriteAllText(Path.Combine(_root, "d3", "x.arb"), "{}");
        Assert.Equal(FormatIds.Arb, detector.Detect(_root));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var _ = ScanContext.Begin(cts.Token, null);
        Assert.Throws<OperationCanceledException>(() => detector.Detect(_root));
    }

    private sealed class SyncProgress(Action<ScanProgress> onReport) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => onReport(value);
    }
}
