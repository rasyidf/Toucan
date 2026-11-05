using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Modules;
using Toucan.Modules.Sources;
using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>Phase 3: the local-folder adapter, and existing projects through the source boundary (R11.1).</summary>
public sealed class LocalFolderSourceTests : IAsyncLifetime
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("toucan-local-source-").FullName;
    private readonly ServiceProvider _services;
    private readonly List<IContentSourceSession> _sessions = [];

    public LocalFolderSourceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        services.AddToucanPlugins(new PluginHostOptions());
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions) await session.DisposeAsync();
        await _services.DisposeAsync();
        TempFolder.TryDelete(_scratch);
    }

    private IContentSourceAdapter Adapter => _services.GetRequiredService<ISourceAdapterCatalog>().Find(LocalFolderSourceAdapter.AdapterId)!.Adapter;

    private async Task<IDocumentSourceSession> OpenAsync(string folder)
    {
        var context = new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = "local-test",
            AdapterId = LocalFolderSourceAdapter.AdapterId,
            BaseUrl = new Uri(folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar),
            RemoteProjectId = folder,
            Settings = new Dictionary<string, object?> { [LocalFolderSourceAdapter.FolderSetting] = folder },
        });
        var session = await Adapter.OpenSessionAsync(context, CancellationToken.None);
        _sessions.Add(session);
        return (IDocumentSourceSession)session;
    }

    private string NewFolder(string name)
    {
        var path = Path.Combine(_scratch, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ───────────── 3.2: the module and the adapter ─────────────

    [Fact]
    public void ModuleRegistersTheAdapterAsABuiltIn()
    {
        var registered = _services.GetRequiredService<ISourceAdapterCatalog>().Find("toucan.sources.local");
        Assert.NotNull(registered);
        Assert.True(registered.IsBuiltIn);
        Assert.Equal("Local folder", registered.Descriptor.DisplayName);
        Assert.Equal(SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.ConditionalWrites, registered.Descriptor.Capabilities);
        Assert.Contains(_services.GetRequiredService<IPluginCatalog>().BuiltInModules, m => m.Id == "toucan.sources");
    }

    [Fact]
    public async Task SessionTestsAndDescribesTheFolder()
    {
        var folder = NewFolder("project");
        var session = await OpenAsync(folder);
        Assert.True((await session.TestAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("project", (await session.DescribeAsync(CancellationToken.None)).Name);

        Directory.Delete(folder);
        var missing = await session.TestAsync(CancellationToken.None);
        Assert.False(missing.Succeeded);
        Assert.Equal(SourceErrorKind.NotFound, missing.Error!.Kind);
    }

    [Fact]
    public async Task OpeningWithoutAFolderIsRefused()
    {
        var context = new SourceTestContext(new SourceConnectionInfo
        {
            ConnectionId = "c", AdapterId = LocalFolderSourceAdapter.AdapterId, BaseUrl = new Uri("file:///"), RemoteProjectId = "x",
        });
        var failure = await Assert.ThrowsAsync<SourceException>(() => Adapter.OpenSessionAsync(context, CancellationToken.None));
        Assert.Equal(SourceErrorKind.Validation, failure.Error.Kind);
    }

    [Fact]
    public async Task ListingIsOrderedPagedCompleteAndSkipsRecoveryAndStagingFiles()
    {
        var folder = NewFolder("project");
        for (var i = 0; i < 450; i++) Write(folder, $"locales/en/f{i:D3}.json", "{}");
        Write(folder, ".toucan/entries/abc.json", "{}");
        Write(folder, ".toucan/recovery/pending/x", "x");
        Write(folder, ".toucan/local/sources/c/cache.json", "{}");
        Write(folder, "locales/en/.f000.json.0123.tmp", "{}");
        var session = await OpenAsync(folder);

        var pages = new List<RemoteDocumentPage>();
        await foreach (var page in session.ListDocumentsAsync(new(), CancellationToken.None)) pages.Add(page);

        Assert.Equal([false, false, true], pages.Select(p => p.IsComplete));
        var paths = pages.SelectMany(p => p.Documents).Select(d => d.Ref.Path).ToList();
        Assert.Equal(451, paths.Count);
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
        Assert.Contains(".toucan/entries/abc.json", paths);
        Assert.DoesNotContain(paths, p => p.Contains("recovery") || p.Contains(".toucan/local") || p.EndsWith(".tmp"));

        var prefixed = new List<string>();
        await foreach (var page in session.ListDocumentsAsync(new("locales/en/f00"), CancellationToken.None)) prefixed.AddRange(page.Documents.Select(d => d.Ref.Path));
        Assert.Equal(10, prefixed.Count);
    }

    [Fact]
    public async Task VersionsMatchTheLocalStoreAndWritesAreConditional()
    {
        var folder = NewFolder("project");
        Write(folder, "a.json", "{\"k\":\"v\"}");
        var session = await OpenAsync(folder);
        var local = new LocalWorkspaceDocumentStore();

        var read = await session.ReadDocumentAsync(new("a.json"), CancellationToken.None);
        Assert.Equal(await local.VersionAsync(folder, "a.json"), read.Version);

        var applied = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("a.json"), "{\"k\":\"w\"}"u8.ToArray(), read.Version), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Applied, applied.Outcome);
        Assert.Equal(await local.VersionAsync(folder, "a.json"), applied.NewVersion);

        var stale = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("a.json"), "{}"u8.ToArray(), read.Version), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Conflict, stale.Outcome);
        Assert.Equal("{\"k\":\"w\"}", File.ReadAllText(Path.Combine(folder, "a.json")));

        var exists = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("a.json"), "{}"u8.ToArray(), null), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Conflict, exists.Outcome);

        var missing = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("b.json"), "{}"u8.ToArray(), "WRONG"), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Conflict, missing.Outcome);
        Assert.False(File.Exists(Path.Combine(folder, "b.json")));

        var created = await session.WriteDocumentAsync(new(Guid.NewGuid(), new("deep/new/b.json"), "{}"u8.ToArray(), null), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Applied, created.Outcome);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DeleteChecksTheVersionAndReportsMissingFiles()
    {
        var folder = NewFolder("project");
        Write(folder, "a.json", "x");
        var session = await OpenAsync(folder);

        var wrong = await session.DeleteDocumentAsync(new("a.json"), "WRONG", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(UnitOperationOutcome.Conflict, wrong.Outcome);
        Assert.True(File.Exists(Path.Combine(folder, "a.json")));

        var version = (await session.ReadDocumentAsync(new("a.json"), CancellationToken.None)).Version;
        Assert.Equal(UnitOperationOutcome.Applied, (await session.DeleteDocumentAsync(new("a.json"), version, Guid.NewGuid(), CancellationToken.None)).Outcome);
        Assert.Equal(UnitOperationOutcome.NotFound, (await session.DeleteDocumentAsync(new("a.json"), version, Guid.NewGuid(), CancellationToken.None)).Outcome);
        var read = await Assert.ThrowsAsync<SourceException>(() => session.ReadDocumentAsync(new("a.json"), CancellationToken.None));
        Assert.Equal(SourceErrorKind.NotFound, read.Error.Kind);
    }

    [Fact]
    public async Task PathsCannotLeaveTheFolderOrPassThroughLinks()
    {
        var folder = NewFolder("project");
        var outside = NewFolder("outside");
        Write(outside, "secret.json", "{}");
        var session = await OpenAsync(folder);

        foreach (var path in new[] { "../outside/secret.json", "a/../../outside/secret.json" })
        {
            var read = await Assert.ThrowsAsync<SourceException>(() => session.ReadDocumentAsync(new(path), CancellationToken.None));
            Assert.Equal(SourceErrorKind.Validation, read.Error.Kind);
            await Assert.ThrowsAsync<SourceException>(() => session.WriteDocumentAsync(new(Guid.NewGuid(), new(path), "x"u8.ToArray(), null), CancellationToken.None));
        }

        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(Path.Combine(folder, "linked"), outside);
            await Assert.ThrowsAsync<SourceException>(() => session.ReadDocumentAsync(new("linked/secret.json"), CancellationToken.None));
            await Assert.ThrowsAsync<SourceException>(() => session.WriteDocumentAsync(new(Guid.NewGuid(), new("linked/x.json"), "x"u8.ToArray(), null), CancellationToken.None));
            var listed = new List<string>();
            await foreach (var page in session.ListDocumentsAsync(new(), CancellationToken.None)) listed.AddRange(page.Documents.Select(d => d.Ref.Path));
            Assert.DoesNotContain(listed, p => p.StartsWith("linked", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task CancelledReadsAndWritesTouchNothing()
    {
        var folder = NewFolder("project");
        Write(folder, "a.json", "x");
        var session = await OpenAsync(folder);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ReadDocumentAsync(new("a.json"), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteDocumentAsync(new(Guid.NewGuid(), new("a.json"), "y"u8.ToArray(), (string?)null), cancelled.Token));
        Assert.Equal("x", File.ReadAllText(Path.Combine(folder, "a.json")));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories));
    }

    // ───────────── 3.3: existing projects through the boundary ─────────────

    private static string RepoRoot
    {
        get
        {
            var path = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(path, "Toucan.CrossPlatform.slnx")))
                path = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Repository root not found.");
            return path;
        }
    }

    private static string[] FixtureNames()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "samples", "babeledit-parity", "manifest.json")));
        return [.. manifest.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];
    }

    public static TheoryData<string> Fixtures() => new(FixtureNames());

    /// <summary>
    /// The framework to open a fixture with and what the direct local path loads from it, or null when the document-aware
    /// path does not apply: the fixture's own framework when a profile of that ID finds its files, otherwise generic JSON for
    /// JSON fixtures (the babeledit names are not all profile IDs). Formats without a document-aware loader (YAML, properties,
    /// PHP, ARB, RESX) stay on the legacy path and are outside the source boundary.
    /// </summary>
    private async Task<(string Framework, WorkspaceSource Source)?> DirectAsync(string fixture, string folder)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "samples", "babeledit-parity", "manifest.json")));
        var entry = manifest.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == fixture);
        var candidates = new List<string> { entry.GetProperty("framework").GetString()! };
        if (entry.GetProperty("kind").GetString() == "json") candidates.Add("generic-json");
        foreach (var framework in candidates.Where(c => _services.GetServices<IFrameworkProfile>().Any(p => p.Id == c)))
        {
            try
            {
                var source = await LoaderOver(null).LoadAsync(folder, framework);
                if (source.Units.Count > 0) return (framework, source);
            }
            catch (NotSupportedException) { }
            catch (FormatUnavailableException) { }
        }
        return null;
    }

    private string Copy(string fixture, string name)
    {
        var target = NewFolder(name);
        var source = Path.Combine(RepoRoot, "samples", "babeledit-parity", fixture);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        return target;
    }

    private WorkspaceSourceLoader LoaderOver(IWorkspaceSourceReader? reader) =>
        new(_services.GetServices<IFrameworkProfile>(), _services.GetServices<ILoadStrategy>(), reader);

    private static List<string> Describe(WorkspaceSource source) =>
    [
        .. source.Documents.OrderBy(d => d.RelativePath, StringComparer.Ordinal)
            .Select(d => $"doc|{d.Id.Value}|{d.RelativePath}|{d.Language}|{d.Package}|{d.FormatId}|{d.Version}|{d.TextFormat}|{d.Content}"),
        .. source.Units.OrderBy(u => u.Binding.DocumentId.Value).ThenBy(u => u.KeyPath.EscapedPath, StringComparer.Ordinal)
            .Select(u => $"unit|{u.EntryId.Value}|{u.Language}|{u.Namespace.EscapedPath}|{u.KeyPath.EscapedPath}|{u.Value}|{u.RawScalar}|{u.Binding.DocumentId.Value}"),
        .. source.Artifacts.OrderBy(a => a.RelativePath, StringComparer.Ordinal).Select(a => $"artifact|{a.RelativePath}|{a.Version}|{a.TextFormat}|{a.Content}"),
    ];

    private static Dictionary<string, string> Hashes(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => (Path: Path.GetRelativePath(folder, f).Replace('\\', '/'), Full: f))
            .Where(f => !f.Path.StartsWith(".toucan/recovery", StringComparison.Ordinal))
            .ToDictionary(f => f.Path, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Full))), StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task FixtureLoadsIdenticallyThroughTheAdapter(string fixture)
    {
        var direct = NewFolder("direct-" + fixture);
        CopyInto(fixture, direct);
        if (await DirectAsync(fixture, direct) is not var (framework, expected)) return;
        var viaReader = Copy(fixture, "reader-" + fixture);
        var viaCache = Copy(fixture, "origin-" + fixture);

        // 1. The loader reading through the adapter's session.
        var readerSession = await OpenAsync(viaReader);
        var loaded = await LoaderOver(new RemoteWorkspaceSourceReader(readerSession)).LoadAsync(viaReader, framework);
        Assert.Equal(Describe(expected), Describe(loaded));

        // 2. The cache a primary connection opens from, pulled from the adapter.
        var cacheSession = await OpenAsync(viaCache);
        var cache = NewFolder("cache-" + fixture);
        var cached = await _services.GetRequiredService<RemoteWorkspaceSnapshotService>().LoadAsync(cacheSession, cache, framework);
        Assert.Equal(Describe(expected), Describe(cached));
        Assert.Equal(Hashes(viaCache).Where(p => !p.Key.StartsWith(".toucan/local", StringComparison.Ordinal)).OrderBy(p => p.Key),
            Hashes(RemoteWorkspaceSnapshotService.FilesRoot(cache)).OrderBy(p => p.Key));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task FixtureSavesIdenticalBytesThroughTheAdapterAndLeavesOtherFilesAlone(string fixture)
    {
        var direct = NewFolder("direct-" + fixture);
        CopyInto(fixture, direct);
        if (await DirectAsync(fixture, direct) is not var (framework, localSource)) return;
        var remote = Copy(fixture, "remote-" + fixture);
        var original = Hashes(remote);
        var planner = _services.GetRequiredService<WorkspaceDocumentSavePlanner>();
        var loaders = _services.GetServices<ILoadStrategy>();

        static WorkspaceDocumentSnapshot Edited(WorkspaceSource source)
        {
            // One edit per document: its first unit gets a suffix, everything else is captured as loaded.
            var first = source.Units.GroupBy(u => u.Binding.DocumentId).Select(g => g.First()).Select(u => (u.EntryId, u.Language)).ToHashSet();
            return new(source.Root, Guid.NewGuid(), 1, source.Units.Select(u => new WorkspaceDocumentUnit(u.EntryId, u.Language, u.Namespace, u.KeyPath,
                first.Contains((u.EntryId, u.Language)) ? u.Value + " (edited)" : u.Value, u.Binding, u.RawScalar)));
        }
        var localPlan = planner.Plan(localSource, Edited(localSource));
        Assert.NotEmpty(localPlan.Targets);
        await new WorkspaceDocumentSaveExecutor(new LocalWorkspaceDocumentStore(), loaders).ExecuteAsync(localPlan);

        var session = await OpenAsync(remote);
        var remoteSource = await LoaderOver(new RemoteWorkspaceSourceReader(session)).LoadAsync(remote, framework);
        var remotePlan = planner.Plan(remoteSource, Edited(remoteSource));
        Assert.Equal(localPlan.Targets.Select(t => (t.RelativePath, t.Operation, t.ExpectedVersion, t.Content)),
            remotePlan.Targets.Select(t => (t.RelativePath, t.Operation, t.ExpectedVersion, t.Content)));
        await new WorkspaceDocumentSaveExecutor(new RemoteWorkspaceDocumentStore(session), loaders).ExecuteAsync(remotePlan);

        var saved = Hashes(remote);
        Assert.Equal(Hashes(direct).OrderBy(p => p.Key), saved.OrderBy(p => p.Key));
        var targeted = remotePlan.Targets.Select(t => t.RelativePath).Concat(remotePlan.Artifacts.Select(a => a.RelativePath)).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(targeted);
        foreach (var (path, hash) in original.Where(p => !targeted.Contains(p.Key)))
            Assert.Equal(hash, saved[path]);
        foreach (var path in remotePlan.Targets.Select(t => t.RelativePath)) Assert.NotEqual(original[path], saved[path]);
    }

    private void CopyInto(string fixture, string target)
    {
        var source = Path.Combine(RepoRoot, "samples", "babeledit-parity", fixture);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    [Fact]
    public async Task ParityCoversTheJsonFixtures()
    {
        // Guards the parity theories above: a fixture that silently falls out of the document path must be noticed.
        var covered = new List<string>();
        foreach (var fixture in FixtureNames())
        {
            var folder = NewFolder("probe-" + fixture);
            CopyInto(fixture, folder);
            if (await DirectAsync(fixture, folder) is not null) covered.Add(fixture);
        }
        Assert.True(covered.Count >= 9, $"Only {covered.Count} fixtures took the document path: {string.Join(", ", covered)}");
    }

    // ───────────── 3.5: older plugins ─────────────

    [Fact]
    public void PluginsLoadedBesideTheBuiltInSourceDoNotChange()
    {
        var root = Path.Combine(_scratch, "plugins-host");
        var dir = Path.Combine(root, "plugins", "sample.tsv");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SamplePlugin")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        var options = new PluginHostOptions();
        options.Roots.Add(Path.Combine(root, "plugins"));
        services.AddToucanPlugins(options);
        using var provider = services.BuildServiceProvider();

        var plugin = Assert.Single(provider.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, plugin.Status);
        var adapters = provider.GetRequiredService<ISourceAdapterCatalog>().Adapters;
        Assert.Equal(["toucan.sources.local", "toucan.sources.rest"], adapters.Select(a => a.Descriptor.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(provider.GetServices<IFrameworkProfile>(), p => p.Id.StartsWith("toucan.sources", StringComparison.Ordinal));
    }
}
