using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Modules;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>Phase 3: the document store and reader over a document session, snapshots, and the capability contracts.</summary>
public sealed class RemoteDocumentStoreTests : IAsyncLifetime
{
    private readonly string _cache = Directory.CreateTempSubdirectory("toucan-remote-cache-").FullName;
    private readonly ServiceProvider _services;
    private readonly MemoryDocumentSession _session = new();

    public RemoteDocumentStoreTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        await _services.DisposeAsync();
        TempFolder.TryDelete(_cache);
    }

    private RemoteWorkspaceSnapshotService Snapshots => _services.GetRequiredService<RemoteWorkspaceSnapshotService>();

    private static WorkspaceDocumentSnapshot Snapshot(WorkspaceSource source, Func<WorkspaceSourceUnit, string> value) =>
        new(source.Root, Guid.NewGuid(), 1, source.Units.Select(u => new WorkspaceDocumentUnit(u.EntryId, u.Language, u.Namespace, u.KeyPath, value(u), u.Binding, u.RawScalar)));

    // ───────────── store and reader ─────────────

    [Fact]
    public async Task VersionsAreContentHashesWhateverTheServiceUses()
    {
        _session.Put("a.json", "{\"k\":\"v\"}");
        var store = new RemoteWorkspaceDocumentStore(_session);
        IWorkspaceSourceReader reader = new RemoteWorkspaceSourceReader(_session);

        Assert.Equal(MemoryDocumentSession.Hash("{\"k\":\"v\"}"), await store.VersionAsync("any", "a.json"));
        Assert.Equal(await store.VersionAsync("any", "a.json"), (await reader.ReadAsync("any", "a.json")).Version);
        Assert.Null(await store.VersionAsync("any", "missing.json"));
        Assert.Null(await reader.ReadOptionalAsync("any", "missing.json"));
    }

    [Fact]
    public async Task ReaderKeepsEncodingAndBom()
    {
        _session.Put("bom.json", "﻿{\"k\":\"é\"}");
        var read = await new RemoteWorkspaceSourceReader(_session).ReadAsync("any", "bom.json");
        Assert.Equal("{\"k\":\"é\"}", read.Content);
        Assert.True(read.TextFormat.HasBom);
    }

    [Fact]
    public async Task WriteIsConditionalOnTheVersionTheCallerSaw()
    {
        _session.Put("a.json", "old");
        var store = new RemoteWorkspaceDocumentStore(_session);
        var seen = await store.VersionAsync("r", "a.json");

        await store.WriteAsync("r", "a.json", "new"u8.ToArray(), seen);
        Assert.Equal("new", _session.Text("a.json"));

        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => store.WriteAsync("r", "a.json", "again"u8.ToArray(), seen));
        Assert.Equal("new", _session.Text("a.json"));
    }

    [Fact]
    public async Task ConcurrentChangeBetweenCheckAndWriteStillConflictsAtTheService()
    {
        _session.Put("a.json", "old");
        var store = new RemoteWorkspaceDocumentStore(_session);
        var seen = await store.VersionAsync("r", "a.json");
        _session.BeforeWrite = path => _session.Put(path, "someone else");

        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => store.WriteAsync("r", "a.json", "mine"u8.ToArray(), seen));
        Assert.Equal("someone else", _session.Text("a.json"));
    }

    [Fact]
    public async Task CreateRequiresAMissingDocument()
    {
        var store = new RemoteWorkspaceDocumentStore(_session);
        await store.WriteAsync("r", "new.json", "x"u8.ToArray(), null);
        Assert.Equal("x", _session.Text("new.json"));
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => store.WriteAsync("r", "new.json", "y"u8.ToArray(), null));
    }

    [Fact]
    public async Task DeleteChecksTheVersion()
    {
        _session.Put("a.json", "x");
        var store = new RemoteWorkspaceDocumentStore(_session);
        await Assert.ThrowsAsync<WorkspaceSourceConflictException>(() => store.DeleteAsync("r", "a.json", "WRONG"));
        Assert.Contains("a.json", _session.Paths);
        await store.DeleteAsync("r", "a.json", MemoryDocumentSession.Hash("x"));
        Assert.DoesNotContain("a.json", _session.Paths);
    }

    [Fact]
    public async Task PathsCannotEscapeTheConnection()
    {
        var store = new RemoteWorkspaceDocumentStore(_session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.VersionAsync("r", "../outside.json"));
    }

    // ───────────── snapshots and refresh ─────────────

    [Fact]
    public async Task FirstRefreshDownloadsEverythingAndLaterOnesOnlyWhatChanged()
    {
        _session.Put("locales/en/common.json", "{\"a\":\"1\"}");
        _session.Put("locales/fr/common.json", "{\"a\":\"un\"}");
        _session.Put("locales/de/common.json", "{\"a\":\"eins\"}");

        var first = await Snapshots.RefreshAsync(_session, _cache);
        Assert.Equal(3, first.Added.Count);
        Assert.Equal(3, _session.Reads);
        Assert.True(RemoteWorkspaceSnapshotService.IsCached(_cache));

        var quiet = await Snapshots.RefreshAsync(_session, _cache);
        Assert.False(quiet.HasChanges);
        Assert.Equal(3, quiet.Unchanged);
        Assert.Equal(3, _session.Reads);

        _session.Put("locales/fr/common.json", "{\"a\":\"uno\"}");
        _session.Put("locales/es/common.json", "{\"a\":\"uno\"}");
        _session.Remove("locales/de/common.json");
        var changed = await Snapshots.RefreshAsync(_session, _cache);
        Assert.Equal(["locales/es/common.json"], changed.Added);
        Assert.Equal(["locales/fr/common.json"], changed.Updated);
        Assert.Equal(["locales/de/common.json"], changed.Removed);
        var files = RemoteWorkspaceSnapshotService.FilesRoot(_cache);
        Assert.Equal("{\"a\":\"uno\"}", File.ReadAllText(Path.Combine(files, "locales/fr/common.json")));
        Assert.False(Directory.Exists(Path.Combine(files, "locales/de")));
    }

    [Fact]
    public async Task CachedProjectOpensWithoutTheNetworkUntilRefreshed()
    {
        _session.Put("locales/en/common.json", "{\"title\":\"Hello\"}");
        _session.Put("locales/fr/common.json", "{\"title\":\"Bonjour\"}");

        var source = await Snapshots.LoadAsync(_session, _cache, "i18next");
        Assert.Equal(2, source.Units.Count);
        var reads = _session.Reads;

        _session.Put("locales/fr/common.json", "{\"title\":\"Salut\"}");
        var offline = await Snapshots.LoadAsync(_session, _cache, "i18next");
        Assert.Equal(reads, _session.Reads);
        Assert.Equal("Bonjour", offline.Units.Single(u => u.Language == "fr").Value);

        var refreshed = await Snapshots.LoadAsync(_session, _cache, "i18next", refresh: true);
        Assert.Equal("Salut", refreshed.Units.Single(u => u.Language == "fr").Value);
    }

    [Fact]
    public async Task OpeningACacheThatWasNeverFilledSaysSo()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Snapshots.OpenCachedAsync(_cache, "i18next"));
    }

    [Fact]
    public async Task IncompleteListingLeavesTheCacheAloneAndNeverInfersDeletion()
    {
        _session.Put("locales/en/a.json", "{\"a\":\"1\"}");
        _session.Put("locales/en/b.json", "{\"b\":\"2\"}");
        _session.Put("locales/en/c.json", "{\"c\":\"3\"}");
        await Snapshots.RefreshAsync(_session, _cache);
        var files = RemoteWorkspaceSnapshotService.FilesRoot(_cache);

        _session.ListingNeverCompletes = true;
        _session.Put("locales/en/a.json", "{\"a\":\"changed\"}");
        var failure = await Assert.ThrowsAsync<SourceException>(() => Snapshots.RefreshAsync(_session, _cache));
        Assert.Equal(SourceErrorKind.Unavailable, failure.Error.Kind);
        Assert.Equal(["a.json", "b.json", "c.json"], Directory.GetFiles(files, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)!).Order().ToArray());
        Assert.Equal("{\"a\":\"1\"}", File.ReadAllText(Path.Combine(files, "locales/en/a.json")));
    }

    [Fact]
    public async Task DuplicateListingsAreRefused()
    {
        _session.Put("a.json", "{}");
        _session.ListsDuplicates = true;
        await Assert.ThrowsAsync<SourceException>(() => Snapshots.RefreshAsync(_session, _cache));
        Assert.False(RemoteWorkspaceSnapshotService.IsCached(_cache));
    }

    [Fact]
    public async Task ListedPathsCannotLeaveTheCache()
    {
        _session.Put("../escape.json", "{}");
        await Assert.ThrowsAnyAsync<Exception>(() => Snapshots.RefreshAsync(_session, _cache));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_cache)!, "escape.json")));
    }

    // ───────────── save previews ─────────────

    [Fact]
    public async Task PreviewListsTheDocumentsASaveWouldChangeAndWritesNothing()
    {
        _session.Put("locales/en/checkout.json", "{\"title\":\"Pay\"}");
        _session.Put("locales/en/profile.json", "{\"title\":\"Profile\"}");
        var source = await Snapshots.LoadAsync(_session, _cache, "i18next");
        var writes = _session.Writes;

        var preview = await Snapshots.PreviewSaveAsync(_session, source, Snapshot(source, u => u.Value == "Pay" ? "Payer" : u.Value));

        Assert.Equal(["locales/en/checkout.json"], preview.Updates);
        Assert.Empty(preview.Creates);
        Assert.Empty(preview.Deletes);
        Assert.Empty(preview.Stale);
        Assert.True(preview.CanSave);
        Assert.Equal(writes, _session.Writes);
        Assert.Equal("{\"title\":\"Pay\"}", _session.Text("locales/en/checkout.json"));
    }

    [Fact]
    public async Task PreviewFlagsDocumentsThatChangedAtTheSourceSinceTheSnapshot()
    {
        _session.Put("locales/en/checkout.json", "{\"title\":\"Pay\"}");
        var source = await Snapshots.LoadAsync(_session, _cache, "i18next");
        _session.Put("locales/en/checkout.json", "{\"title\":\"Pay now\"}");

        var preview = await Snapshots.PreviewSaveAsync(_session, source, Snapshot(source, _ => "Payer"));

        Assert.Equal(["locales/en/checkout.json"], preview.Stale);
        Assert.False(preview.CanSave);
    }

    // ───────────── contracts ─────────────

    [Fact]
    public async Task ReadOnlySourceRefusesWritesBeforeAnythingIsSent()
    {
        _session.Put("locales/en/checkout.json", "{\"title\":\"Pay\"}");
        var source = await Snapshots.LoadAsync(_session, _cache, "i18next");
        var store = new RemoteWorkspaceDocumentStore(_session, SourceCapabilities.ReadDocuments);

        var preview = await Snapshots.PreviewSaveAsync(_session, source, Snapshot(source, _ => "Payer"), capabilities: SourceCapabilities.ReadDocuments);
        Assert.NotNull(preview.Refusal);
        Assert.False(preview.CanSave);

        var write = await Assert.ThrowsAsync<SourceException>(() => store.WriteAsync("r", "locales/en/checkout.json", "x"u8.ToArray(), null));
        var delete = await Assert.ThrowsAsync<SourceException>(() => store.DeleteAsync("r", "locales/en/checkout.json", "v"));
        Assert.Equal(SourceErrorKind.Unsupported, write.Error.Kind);
        Assert.Equal(SourceErrorKind.Unsupported, delete.Error.Kind);
        Assert.Equal(0, _session.Writes + _session.Deletes);
        Assert.NotNull(await store.VersionAsync("r", "locales/en/checkout.json"));
    }

    [Fact]
    public async Task SourceWithoutReadCapabilityIsNeverRead()
    {
        _session.Put("a.json", "{}");
        var reader = new RemoteWorkspaceSourceReader(_session, SourceCapabilities.None);
        var store = new RemoteWorkspaceDocumentStore(_session, SourceCapabilities.WriteDocuments);
        await Assert.ThrowsAsync<SourceException>(() => reader.ReadAsync("r", "a.json"));
        await Assert.ThrowsAsync<SourceException>(() => store.VersionAsync("r", "a.json"));
        Assert.Equal(0, _session.Reads);
    }

    [Fact]
    public async Task SameKeyInDifferentPackagesStaysSeparateThroughTheSource()
    {
        _session.Put("locales/en/checkout.json", "{\"title\":\"Pay\"}");
        _session.Put("locales/en/profile.json", "{\"title\":\"Profile\"}");
        _session.Put("locales/fr/checkout.json", "{\"title\":\"Payer\"}");
        _session.Put("locales/fr/profile.json", "{\"title\":\"Profil\"}");
        var source = await Snapshots.LoadAsync(_session, _cache, "i18next");
        Assert.Equal(4, source.Units.Count);
        Assert.Equal(2, source.Units.Select(u => u.EntryId).Distinct().Count());

        var edited = Snapshot(source, u => u.Language == "fr" && u.Value == "Profil" ? "Mon profil" : u.Value);
        var preview = await Snapshots.PreviewSaveAsync(_session, source, edited);
        Assert.Equal(["locales/fr/profile.json"], preview.Updates);
        Assert.Equal("{\"title\":\"Profil\"}", _session.Text("locales/fr/profile.json"));
    }
}
