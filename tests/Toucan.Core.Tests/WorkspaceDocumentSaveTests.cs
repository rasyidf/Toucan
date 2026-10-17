using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class WorkspaceDocumentSaveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-document-save-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceDocumentSavePlanner _planner;

    public WorkspaceDocumentSaveTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _planner = _services.GetRequiredService<WorkspaceDocumentSavePlanner>();
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Task<WorkspaceSource> Load() => _services.GetRequiredService<WorkspaceSourceLoader>().LoadAsync(_root, "i18next");
    private static WorkspaceDocumentUnit Unit(WorkspaceSourceUnit unit) => new(unit.EntryId, unit.Language, unit.Namespace,
        unit.KeyPath, unit.Value, unit.Binding, unit.RawScalar);
    private WorkspaceDocumentSnapshot Snapshot(WorkspaceSource source, IEnumerable<WorkspaceDocumentUnit>? units = null) =>
        new(_root, Guid.NewGuid(), 42, units ?? source.Units.Select(Unit));
    private IDocumentSaveStrategy Writer => (IDocumentSaveStrategy)_services.GetServices<ISaveStrategy>().Single(s => s.FormatId == FormatIds.Json);

    [Fact]
    public async Task NoChangesProduceNoTargetsOrFilesystemWrites()
    {
        Write("locales/en/common.json", " { \"title\" : \"Hello\", \"null\":null, \"empty\":{} }\n");
        var source = await Load();
        var path = Path.Combine(_root, source.Documents.Single().RelativePath);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var plan = _planner.Plan(source, Snapshot(source));
        Assert.Empty(plan.Targets);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(source.Documents.Single().Content, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(_root, ".toucan")));
    }

    [Fact]
    public async Task EditingOneDuplicateKeyTargetsOnlyItsOwnerAndRetainsSourceText()
    {
        const string content = "{\r\n\t\"title\" : \"Hello\",\r\n\t\"number\":1e+02,\r\n\t\"untouched\":\"\\u00e9\",\r\n\t\"null\":null, \"empty\":{}, \"array\":[true,2]\r\n}\r\n";
        Write("locales/en/checkout.json", content);
        Write("locales/fr/checkout.json", "{\"title\":\"Bonjour\"}");
        Write("locales/en/profile.json", "{\"title\":\"Profile\"}");
        var source = await Load();
        var snapshot = Snapshot(source, source.Units.Select(Unit).Select(u => u.Value == "Hello" ? u with { Value = "Salut 🎉" } : u));
        var plan = _planner.Plan(source, snapshot);
        var target = Assert.Single(plan.Targets);
        Assert.Equal("locales/en/checkout.json", target.RelativePath);
        Assert.Equal(WorkspaceDocumentSaveOperation.Update, target.Operation);
        Assert.Equal(source.Documents.Single(d => d.Id == target.DocumentId).Version, target.ExpectedVersion);
        Assert.Equal(content.Replace("\"Hello\"", JsonSerializer.Serialize("Salut 🎉"), StringComparison.Ordinal), target.Content);
        Assert.Equal(snapshot.SessionId, plan.SessionId);
        Assert.Equal(42, plan.CapturedRevision);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_root, target.RelativePath)));
    }

    [Fact]
    public async Task EngineSnapshotUsesIdentityAndOriginalBindingAfterRename()
    {
        Write("locales/en/common.json", "{\"a.b\":\"Literal\",\"a\":{\"b\":\"Nested\"},\"null\":null}");
        var source = await Load();
        var engine = _services.GetRequiredService<WorkspaceService>();
        engine.Open(new() { ProjectPath = _root, PrimaryLanguage = "en", Languages = ["en"] }, source);
        var old = source.Units.Single(u => u.Value == "Literal");
        var snapshot = (await engine.SnapshotEntriesAsync())!;
        await engine.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision,
            [new Toucan.Plugins.RenameEntry(old.EntryId.Value, old.Namespace, new(["renamed"]))]));
        var captured = engine.CaptureDocumentSnapshot()!;
        var target = Assert.Single(_planner.Plan(source, captured).Targets);
        using var json = JsonDocument.Parse(target.Content!);
        Assert.False(json.RootElement.TryGetProperty("a.b", out _));
        Assert.Equal("Literal", json.RootElement.GetProperty("renamed").GetString());
        Assert.Equal("Nested", json.RootElement.GetProperty("a").GetProperty("b").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("null").ValueKind);
        Assert.Equal(old.Binding, captured.Units.Single(u => u.EntryId == old.EntryId).Binding);
    }

    [Fact]
    public async Task ExplicitMoveDeclaresBothDocumentsAndKeepsEntryIdentity()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\",\"unrelated\":null}");
        Write("locales/en/profile.json", "{\"other\":\"Profile\"}");
        var source = await Load();
        var moved = source.Units.Single(u => u.Value == "Checkout");
        var snapshot = Snapshot(source, source.Units.Select(Unit).Select(u => u.EntryId == moved.EntryId
            ? u with { Namespace = new(["profile"]), KeyPath = new(["moved"]) } : u));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, snapshot));
        var plan = _planner.Plan(source, snapshot, [new(moved.EntryId, "en", "locales/en/profile.json")]);
        Assert.Equal(2, plan.Targets.Count);
        Assert.All(plan.Targets, t => Assert.Equal(WorkspaceDocumentSaveOperation.Update, t.Operation));
        var saved = plan.Units.Single(u => u.EntryId == moved.EntryId);
        Assert.Equal(source.Documents.Single(d => d.RelativePath.EndsWith("profile.json", StringComparison.Ordinal)).Id, saved.Binding.DocumentId);
        Assert.Equal(["moved"], saved.Binding.Location.Segments);
    }

    [Fact]
    public async Task NewEntriesRequireAnUnambiguousDestinationAndDeclareMissingVersion()
    {
        Write("locales/en/common.json", "{\"title\":\"Common\"}");
        Write("locales/en/other.json", "{\"title\":\"Other\"}");
        var source = await Load();
        var added = new WorkspaceDocumentUnit(new(Guid.NewGuid()), "en", new(["new"]), new(["new"]), "New", null);
        var snapshot = Snapshot(source, source.Units.Select(Unit).Append(added));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, snapshot));
        var plan = _planner.Plan(source, snapshot, [new(added.EntryId, "en", "locales/en/new.json")]);
        var target = Assert.Single(plan.Targets);
        Assert.Equal(WorkspaceDocumentSaveOperation.Create, target.Operation);
        Assert.Null(target.ExpectedVersion);
        Assert.Equal("locales/en/new.json", target.RelativePath);
        Assert.False(File.Exists(Path.Combine(_root, target.RelativePath)));
        Assert.Equal(added.EntryId, plan.Units.Single(u => u.Value == "New").EntryId);
        // Simulate execution only in this fixture and check the discovery contract agrees with planning.
        File.WriteAllText(Path.Combine(_root, target.RelativePath), target.Content);
        var reopened = (await Load()).Units.Single(u => u.Value == "New");
        Assert.Equal(added.Namespace, reopened.Namespace);
        Assert.Equal(added.KeyPath, reopened.KeyPath);
    }

    [Fact]
    public async Task RemovingLastEntryPreservesDocumentUnlessDeletionIsExplicit()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\",\"null\":null,\"empty\":[]}");
        var source = await Load();
        var snapshot = Snapshot(source, []);
        var update = Assert.Single(_planner.Plan(source, snapshot).Targets);
        Assert.Equal(WorkspaceDocumentSaveOperation.Update, update.Operation);
        using var json = JsonDocument.Parse(update.Content!);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        var deletion = Assert.Single(_planner.Plan(source, snapshot, deletedDocuments: [source.Documents.Single().Id]).Targets);
        Assert.Equal(WorkspaceDocumentSaveOperation.Delete, deletion.Operation);
        Assert.Null(deletion.Content);
        Assert.NotNull(deletion.ExpectedVersion);
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, Snapshot(source), deletedDocuments: [source.Documents.Single().Id]));
    }

    [Fact]
    public async Task PlanningFreezesInputAndNeverChangesTheCapturedSource()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Load();
        var units = source.Units.Select(Unit).Select(u => u with { Value = "Planned" }).ToList();
        var snapshot = Snapshot(source, units);
        units.Clear();
        var plan = _planner.Plan(source, snapshot);
        Assert.Contains("Planned", Assert.Single(plan.Targets).Content, StringComparison.Ordinal);
        Assert.Equal("Hello", Assert.Single(source.Units).Value);
        Assert.Equal("Planned", Assert.Single(plan.Units).Value);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData(".toucan/identity.json")]
    [InlineData("locales/fr/common.json")]
    public async Task InvalidDestinationsCannotClaimOtherResources(string destination)
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        var source = await Load();
        var en = source.Units.Single(u => u.Language == "en");
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, Snapshot(source), [new(en.EntryId, "en", destination)]));
    }

    [Fact]
    public async Task UnsupportedSerializerCannotFallBackToLegacyWriting()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Load();
        var legacy = Substitute.For<ISaveStrategy>();
        legacy.FormatId.Returns(source.FormatId);
        var planner = new WorkspaceDocumentSavePlanner(_services.GetRequiredService<WorkspaceDocumentRouter>(), [legacy]);
        Assert.Throws<NotSupportedException>(() => planner.Plan(source, Snapshot(source, source.Units.Select(Unit).Select(u => u with { Value = "Changed" }))));
        legacy.DidNotReceiveWithAnyArgs().Save(default!, default!);
    }

    [Fact]
    public async Task DuplicateDestinationsAndChangedOriginalBindingsAreRefused()
    {
        Write("locales/en/common.json", "{\"a\":\"A\",\"b\":\"B\"}");
        var source = await Load();
        var colliding = Snapshot(source, source.Units.Select(Unit).Select(u => u with { KeyPath = new(["collision"]) }));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, colliding));
        var rebound = Snapshot(source, source.Units.Select(Unit).Select(u => u with { Binding = new(new(Guid.NewGuid()), u.KeyPath) }));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, rebound));
    }

    [Fact]
    public void ValueEditsPreserveArraySlotsUnicodeAndAllUneditedBytes()
    {
        const string content = "{ \"é\": [\"old\", null, 1e+02, true], \"empty\":{} }\n";
        var path = new TranslationPath(["é", "0"]);
        var serialized = Writer.SerializeDocument(content, [new(path, new(path, "New 🎉"))]);
        Assert.Equal(content.Replace("\"old\"", JsonSerializer.Serialize("New 🎉"), StringComparison.Ordinal), serialized);
    }

    [Fact]
    public void StructuralEditsRetainEmptyContainersRawScalarsAndTextConventions()
    {
        const string content = "{\r\n\t\"old\":\"Value\",\r\n\t\"number\":1e+02,\r\n\t\"null\":null,\r\n\t\"empty\":{}\r\n}\r\n";
        var serialized = Writer.SerializeDocument(content, [new(new(["old"]), new(new(["renamed"]), "Value"))]);
        Assert.Contains("\r\n\t\"renamed\"", serialized, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", serialized.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal("1e+02", json.RootElement.GetProperty("number").GetRawText());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("null").ValueKind);
        Assert.Empty(json.RootElement.GetProperty("empty").EnumerateObject());
    }

    [Fact]
    public void StructuralArraySlotChangesAreRefusedWithoutShiftingSiblingIdentities()
    {
        Assert.Throws<NotSupportedException>(() => Writer.SerializeDocument("{\"items\":[\"a\",\"b\"]}", [new(new(["items", "0"]), null)]));
    }

    [Theory]
    [InlineData("{\"key\":\"a\",\"key\":\"b\"}")]
    [InlineData("[]")]
    [InlineData("{\"key\":\"a\"} {}")]
    public void InvalidDocumentsAreRefusedEvenForNoChanges(string content) =>
        Assert.ThrowsAny<JsonException>(() => Writer.SerializeDocument(content, []));

    [Fact]
    public void RenameCannotOverwriteUneditedNullOrContainer()
    {
        Assert.Throws<InvalidOperationException>(() => Writer.SerializeDocument("{\"a\":\"A\",\"b\":null}", [new(new(["a"]), new(new(["b"]), "A"))]));
        Assert.Throws<InvalidOperationException>(() => Writer.SerializeDocument("{\"a\":\"A\",\"b\":{}}", [new(new(["a"]), new(new(["b"]), "A"))]));
    }

    [Fact]
    public async Task ReaderRetainsEncodingBomAndVersionForExecution()
    {
        var path = Path.Combine(_root, "document.json");
        await File.WriteAllTextAsync(path, "{\"title\":\"Hello\"}", Encoding.Unicode);
        var reader = _services.GetRequiredService<IWorkspaceSourceReader>();
        var content = await reader.ReadAsync(_root, "document.json");
        Assert.Equal("utf-16", content.TextFormat.EncodingName);
        Assert.True(content.TextFormat.HasBom);
        Assert.Equal("{\"title\":\"Hello\"}", content.Content);
        Assert.Equal(SaveTransaction.Fingerprint([path])[path], content.Version);
    }

    [Fact]
    public async Task SourceReaderCanBeReplacedWithoutChangingLayoutOrSerialization()
    {
        Write("locales/en/common.json", "{\"title\":\"Disk\"}");
        var reader = Substitute.For<IWorkspaceSourceReader>();
        reader.ReadAsync(_root, "locales/en/common.json", Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new WorkspaceSourceContent("{\"title\":\"Source\"}", "source-version", SourceTextFormat.Utf8)));
        var loader = new WorkspaceSourceLoader(_services.GetServices<Toucan.Core.Contracts.IFrameworkProfile>(),
            _services.GetServices<ILoadStrategy>(), reader);
        var source = await loader.LoadAsync(_root, "i18next");
        Assert.Equal("Source", Assert.Single(source.Units).Value);
        Assert.Equal("source-version", Assert.Single(source.Documents).Version);
        var target = Assert.Single(_planner.Plan(source, Snapshot(source, source.Units.Select(Unit).Select(u => u with { Value = "Changed" }))).Targets);
        Assert.Equal("source-version", target.ExpectedVersion);
        Assert.Contains("Disk", File.ReadAllText(Path.Combine(_root, target.RelativePath)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewDestinationMustBeDiscoverableInTheRequestedNamespaceAndLanguage()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Load();
        var added = new WorkspaceDocumentUnit(new(Guid.NewGuid()), "en", new(["new"]), new(["added"]), "Added", null);
        var snapshot = Snapshot(source, source.Units.Select(Unit).Append(added));
        Assert.Throws<NotSupportedException>(() => _planner.Plan(source, snapshot, [new(added.EntryId, "en", "unknown/en/new.json")]));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, snapshot, [new(added.EntryId, "en", "locales/fr/new.json")]));
        Assert.Throws<InvalidOperationException>(() => _planner.Plan(source, snapshot, [new(added.EntryId, "en", "locales/en/other.json")]));
    }

    [Fact]
    public void AtomicRenameCanSwapAddressesWithoutOverwritingData()
    {
        var serialized = Writer.SerializeDocument("{\"a\":\"A\",\"b\":\"B\"}", [
            new(new(["a"]), new(new(["b"]), "A")), new(new(["b"]), new(new(["a"]), "B"))]);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal("B", json.RootElement.GetProperty("a").GetString());
        Assert.Equal("A", json.RootElement.GetProperty("b").GetString());
    }

    [Fact]
    public void EditingTypedScalarsProducesStringsAndPreservesUneditedTypes()
    {
        var serialized = Writer.SerializeDocument("{\"number\":2,\"flag\":true}", [new(new(["number"]), new(new(["number"]), "Two", "2"))]);
        using var json = JsonDocument.Parse(serialized);
        Assert.Equal("Two", json.RootElement.GetProperty("number").GetString());
        Assert.True(json.RootElement.GetProperty("flag").GetBoolean());
    }

    [Fact]
    public async Task CancelledPlanningDoesNotWriteAnything()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        var source = await Load();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => _planner.Plan(source, Snapshot(source), cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => Writer.SerializeDocument("{}", [], cancellation.Token));
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
