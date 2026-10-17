using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-workspace-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public WorkspaceSourceTests()
    {
        Directory.CreateDirectory(_root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
    }

    private void Write(string path, string content)
    {
        var absolute = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
    }

    private Task<WorkspaceSource> Load(CancellationToken ct = default) =>
        _services.GetRequiredService<WorkspaceSourceLoader>().LoadAsync(_root, "i18next", cancellationToken: ct);

    [Fact]
    public async Task DuplicateKeysKeepDocumentOwnershipAndLanguageFamilies()
    {
        Write("public/locales/en/checkout.json", """{"errors":{"required":"Checkout"}}""");
        Write("public/locales/fr/checkout.json", """{"errors":{"required":"Paiement"}}""");
        Write("public/locales/en/profile.json", """{"errors":{"required":"Profile"}}""");
        var source = await Load();
        Assert.Equal(3, source.Documents.Count);
        var checkout = source.Units.Where(u => u.Namespace.Segments.Single() == "checkout").ToList();
        Assert.Equal(checkout[0].EntryId, checkout[1].EntryId);
        Assert.NotEqual(checkout[0].Binding.DocumentId, checkout[1].Binding.DocumentId);
        Assert.NotEqual(checkout[0].EntryId, source.Units.Single(u => u.Value == "Profile").EntryId);
        Assert.Equal(["errors", "required"], checkout[0].KeyPath.Segments);
    }

    [Fact]
    public async Task LiteralDotsSlashesAndArrayLocationsAreNotFlattened()
    {
        Write("locales/en/common.json", """{"a.b":"literal","a":{"b":"nested"},"a/b~":["first",2,true,null],"empty":{}}""");
        var source = await Load();
        var literal = source.Units.Single(u => u.Value == "literal");
        var nested = source.Units.Single(u => u.Value == "nested");
        Assert.Equal(["a.b"], literal.KeyPath.Segments);
        Assert.Equal(["a", "b"], nested.KeyPath.Segments);
        Assert.NotEqual(literal.EntryId, nested.EntryId);
        Assert.Equal("/a~1b~0/0", source.Units.Single(u => u.Value == "first").Binding.Location.EscapedPath);
        Assert.Equal("2", source.Units.Single(u => u.Value == "2").RawScalar);
        Assert.Contains("\"empty\":{}", source.Documents.Single().Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityIsStableOnReopenWhileSourceVersionChanges()
    {
        Write("locales/en/common.json", """{"title":"Before"}""");
        var before = await Load();
        Write("locales/en/common.json", """{"title":"After"}""");
        var after = await Load();
        Assert.Equal(before.Documents.Single().Id, after.Documents.Single().Id);
        Assert.Equal(before.Units.Single().EntryId, after.Units.Single().EntryId);
        Assert.NotEqual(before.Documents.Single().Version, after.Documents.Single().Version);
        var renamed = after.Units.Single() with { KeyPath = new(["renamed"]) };
        Assert.Equal(after.Units.Single().EntryId, renamed.EntryId);
        Assert.Equal(after.Units.Single().Binding, renamed.Binding);
    }

    [Fact]
    public async Task SingleFileLanguageLayoutLoadsWithoutLanguageDirectories()
    {
        Write("locales/en.json", """{"title":"Hello"}""");
        Write("locales/fr.json", """{"title":"Bonjour"}""");
        var source = await Load();
        Assert.Equal(2, source.Documents.Count);
        Assert.Equal(source.Units[0].EntryId, source.Units[1].EntryId);
        Assert.Empty(source.Units[0].Namespace.Segments);
        var router = _services.GetRequiredService<WorkspaceDocumentRouter>();
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "de"));
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "de", relativeDestination: "locales/en.json"));
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "de", relativeDestination: "../outside.json"));
    }

    [Fact]
    public async Task IdentityCheckpointRetainsRenamedIdentityAcrossReopenAndSerialization()
    {
        Write("locales/en/common.json", """{"old.name":"Hello"}""");
        var before = await Load();
        var checkpoint = new WorkspaceIdentityCheckpoint(before.Root,
            before.IdentityCatalog.Select(i => i with { KeyPath = new(["new.name"]) }).ToArray());
        var serialized = System.Text.Json.JsonSerializer.Serialize(checkpoint);
        var restored = System.Text.Json.JsonSerializer.Deserialize<WorkspaceIdentityCheckpoint>(serialized)!;
        Write("locales/en/common.json", """{"new.name":"Hello"}""");
        var after = await _services.GetRequiredService<WorkspaceSourceLoader>()
            .LoadAsync(_root, "i18next", identityCheckpoint: restored);
        Assert.Equal(before.Units.Single().EntryId, after.Units.Single().EntryId);
        Assert.Equal(["new.name"], after.Units.Single().KeyPath.Segments);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _services.GetRequiredService<WorkspaceSourceLoader>()
            .LoadAsync(_root, "i18next", identityCheckpoint: restored with { Root = Path.Combine(_root, "other") }));
    }

    [Fact]
    public async Task SeparateLocaleRootsDoNotShareEntryIdentity()
    {
        Write("public/locales/en/common.json", """{"title":"Public"}""");
        Write("src/locales/en/common.json", """{"title":"Source"}""");
        var source = await Load();
        Assert.NotEqual(source.Units[0].EntryId, source.Units[1].EntryId);
        var router = _services.GetRequiredService<WorkspaceDocumentRouter>();
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "en", "common"));
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "de", "common"));
        Assert.Equal(Path.Combine(_root, "public/locales/de/common.json"),
            router.NewEntryPath(source, "de", "common", "public/locales/de/common.json"));
    }

    [Fact]
    public async Task ExistingRoutingKeepsExactDiscoveredLocationAfterRename()
    {
        Write("public/locales/en/checkout.json", """{"title":"Public"}""");
        Write("locales/en/other.json", """{"title":"Other"}""");
        var source = await Load();
        var unit = source.Units.Single(u => u.Value == "Public") with { KeyPath = new(["new.name"]) };
        var router = _services.GetRequiredService<WorkspaceDocumentRouter>();
        Assert.Equal(Path.Combine(_root, "public/locales/en/checkout.json"), router.ExistingPath(source, unit));
        Assert.Equal(router.ExistingPath(source, unit), router.NewEntryPath(source, "en", "checkout"));
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "de", "checkout"));
        Assert.Throws<InvalidOperationException>(() => router.NewEntryPath(source, "en", "new-package"));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("{\"title\":\"first\",\"title\":\"second\"}")]
    [InlineData("[]")]
    public async Task InvalidDocumentFailsInsteadOfReturningPartialState(string content)
    {
        Write("locales/en/common.json", content);
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Load());
    }

    [Fact]
    public async Task ExplicitCapabilitiesDoNotFallBack()
    {
        var loader = _services.GetRequiredService<WorkspaceSourceLoader>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(_root, "missing-framework"));
        await Assert.ThrowsAsync<FormatUnavailableException>(() => loader.LoadAsync(_root, "i18next", "missing-format"));
        await Assert.ThrowsAsync<NotSupportedException>(() => loader.LoadAsync(_root, "i18next", FormatIds.Arb));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => loader.LoadAsync(Path.Combine(_root, "missing"), "i18next"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Load(cancelled.Token));
    }

    [Fact]
    public async Task ThirdPartyProfileWithoutGroupingKeepsDocumentsIndependent()
    {
        Write("en.json", """{"title":"Hello"}""");
        Write("fr.json", """{"title":"Bonjour"}""");
        var loader = new WorkspaceSourceLoader([new SimpleProfile()], _services.GetServices<ILoadStrategy>());
        var source = await loader.LoadAsync(_root, "third-party");
        Assert.NotEqual(source.Units[0].EntryId, source.Units[1].EntryId);
        Assert.Equal(["title"], source.Units[0].KeyPath.Segments);
    }

    [Fact]
    public void StructuredPathsCopySegmentsAndCompareByValue()
    {
        var segments = new[] { "a.b", "c" };
        var path = new TranslationPath(segments);
        segments[0] = "changed";
        Assert.Equal(new TranslationPath(["a.b", "c"]), path);
        Assert.NotEqual(new TranslationPath(["a", "b", "c"]), path);
    }

    private sealed class SimpleProfile : IFrameworkProfile
    {
        public string Id => "third-party";
        public string DisplayName => Id;
        public string DefaultFormatId => FormatIds.Json;
        public IEnumerable<string> FilePatterns => ["*.json"];
        public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder) =>
            Directory.EnumerateFiles(rootFolder, "*.json").Select(p => new DiscoveredFile(p, Path.GetFileName(p), Path.GetFileNameWithoutExtension(p)));
        public string? ExtractLanguage(string relativePath) => Path.GetFileNameWithoutExtension(relativePath);
        public string GetFilePath(string rootFolder, string language, string? package = null) => Path.Combine(rootFolder, language + ".json");
        public int DetectionScore(string rootFolder) => 0;
    }

    public void Dispose()
    {
        _services.Dispose();
        TempFolder.TryDelete(_root);
    }
}
