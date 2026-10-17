using System.Security.Cryptography;
using System.Text;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class FileWatcherVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-watcher-" + Guid.NewGuid().ToString("N"));
    private readonly FileWatcherService _watcher = new();

    public FileWatcherVersionTests() => Directory.CreateDirectory(_root);
    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }
    private static string Version(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void SameTimestampAndLengthStillDetectsContentChange()
    {
        Write("en.json", "before"); var path = Path.Combine(_root, "en.json"); var timestamp = File.GetLastWriteTimeUtc(path);
        _watcher.Watch(_root);
        Write("en.json", "after!"); File.SetLastWriteTimeUtc(path, timestamp);
        var change = Assert.Single(_watcher.GetChanges());
        Assert.Equal("en.json", change.RelativePath); Assert.Equal(Version("before"), change.PreviousVersion); Assert.Equal(Version("after!"), change.CurrentVersion);
    }

    [Fact]
    public void SaveAcknowledgesOnlyDeclaredTargetsAndDetectsLaterEdits()
    {
        Write("en.json", "old"); Write("fr.json", "original"); _watcher.Watch(_root);
        Write("en.json", "saved"); Write("fr.json", "external");
        _watcher.AcknowledgeWrites(_root, [new("en.json", Version("old"), Version("saved"))]);
        Assert.Equal("fr.json", Assert.Single(_watcher.GetChanges()).RelativePath);
        Write("en.json", "newer external");
        var changed = _watcher.GetChanges().Single(c => c.RelativePath == "en.json");
        Assert.Equal(Version("saved"), changed.PreviousVersion); Assert.Equal(Version("newer external"), changed.CurrentVersion);
    }

    [Fact]
    public void DeletedTargetsAreAcknowledgedWithoutHidingUnrelatedCreation()
    {
        Write("en.json", "old"); _watcher.Watch(_root);
        File.Delete(Path.Combine(_root, "en.json")); Write("fr.json", "new");
        Assert.Contains(_watcher.GetChanges(), c => c.RelativePath == "en.json" && c.CurrentVersion is null);
        _watcher.AcknowledgeWrites(_root, [new("en.json", Version("old"), null)]);
        Assert.Equal("fr.json", Assert.Single(_watcher.GetChanges()).RelativePath);
    }

    [Fact]
    public void OwnedMetadataIsObservedButDraftsAndJournalsAreIgnored()
    {
        _watcher.Watch(_root);
        Write(".toucan/entries/entry.json", "metadata"); Write(".toucan/workspace-identities.json", "identities");
        Write(".toucan/recovery-draft.json", "draft"); Write(".toucan/journal/backup.json", "journal");
        Write(".en.guid.tmp", "stage");
        Assert.Equal([".toucan/entries/entry.json", ".toucan/workspace-identities.json"], _watcher.GetChanges().Select(c => c.RelativePath));
    }

    [Fact]
    public async Task ReplacingWatchDuringFileEventsReleasesNativeResources()
    {
        await Task.Run(() =>
        {
            for (var i = 0; i < 30; i++)
            {
                _watcher.Watch(_root);
                Write("en.json", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _watcher.Stop();
            }
        }).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(_watcher.GetChanges());
    }

    [Fact]
    public void OldRootAcknowledgmentCannotChangeReplacementBaseline()
    {
        Write("en.json", "original"); _watcher.Watch(_root);
        var previousGeneration = _watcher.WatchGeneration;
        var next = Path.Combine(_root, "replacement"); Directory.CreateDirectory(next);
        File.WriteAllText(Path.Combine(next, "en.json"), "next"); _watcher.Watch(next);
        Assert.NotEqual(previousGeneration, _watcher.WatchGeneration);
        _watcher.AcknowledgeWrites(_root, [new("en.json", null, Version("old session"))]);
        Assert.Empty(_watcher.GetChanges());
        _watcher.Stop(); Assert.Empty(_watcher.GetChanges());
    }

    public void Dispose() { _watcher.Dispose(); Directory.Delete(_root, true); }
}
