using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>Destination choices for new entries, moves, and language creation/removal, end to end through the save coordinator.</summary>
public sealed class WorkspaceDestinationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-destinations-").FullName;
    private readonly ServiceProvider _services;
    private readonly WorkspaceService _workspace;
    private readonly WorkspaceDocumentRouter _router;

    public WorkspaceDestinationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        _workspace = _services.GetRequiredService<WorkspaceService>();
        _router = _services.GetRequiredService<WorkspaceDocumentRouter>();
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));
    private bool Exists(string relative) => File.Exists(Path.Combine(_root, relative));

    private async Task Open(params string[] languages)
    {
        var source = await _services.GetRequiredService<WorkspaceSourceLoader>().LoadAsync(_root, "i18next");
        _workspace.Open(new() { ProjectPath = _root, PrimaryLanguage = "en", Languages = languages.ToList() }, source);
    }
    private Task<WorkspaceDocumentSaveResult> Save() =>
        _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(_workspace.Source!);
    private IReadOnlyList<WorkspaceDestinationFamily> Families(params string[] languages) => _router.DestinationFamilies(_workspace.Source!, languages);
    private async Task<IdentifiedUnitSnapshot> Unit(string value) => (await _workspace.SnapshotEntriesAsync())!.Units.Single(u => u.Value == value);

    [Fact]
    public async Task FamiliesGroupLanguagesAndPlaceMissingOnesThroughTheFramework()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}");
        Write("locales/fr/checkout.json", "{\"title\":\"Paiement\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        await Open("en", "fr");
        var families = Families("en", "fr", "de");
        Assert.Equal(["checkout", "profile"], families.Select(f => f.Label));
        var checkout = families[0];
        Assert.Equal(new TranslationPath(["checkout"]), checkout.Namespace);
        Assert.Equal("locales/en/checkout.json", checkout.Documents["en"]);
        Assert.Equal("locales/de/checkout.json", checkout.Documents["de"]);
        Assert.Equal("locales/fr/profile.json", families[1].Documents["fr"]);
        Assert.Equal(["locales/en/checkout.json", "locales/fr/checkout.json"], checkout.ExistingDocuments.Order());
        Assert.False(Exists("locales/de/checkout.json"));
    }

    [Fact]
    public async Task LanguageFileLayoutsSubstituteTheLanguageFileName()
    {
        Write("locales/en.json", "{\"title\":\"Hello\"}");
        await Open("en");
        var family = Assert.Single(Families("pt-BR"));
        Assert.Equal("locales/pt-BR.json", family.Documents["pt-BR"]);
        Assert.Equal(TranslationPath.Empty, family.Namespace);
    }

    [Fact]
    public async Task NewEntryInAnAmbiguousLayoutSavesToTheChosenFamilyOnly()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}\n");
        Write("locales/fr/checkout.json", "{\"title\":\"Paiement\"}\n");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}\n");
        await Open("en", "fr");
        var profile = Read("locales/en/profile.json");
        var checkout = Families().Single(f => f.Label == "checkout");
        var added = _workspace.AddDocumentEntry(checkout, new(["pay.now"]), new Dictionary<string, string> { ["en"] = "Pay now" });
        Assert.True(added.Applied);
        Assert.Equal(2, added.ChangedUnits);
        Assert.True(_workspace.IsDirty);

        var result = await Save();
        Assert.True(result.Acknowledged);
        Assert.Equal(["locales/en/checkout.json", "locales/fr/checkout.json"], result.Receipt.Plan.Targets.Select(t => t.RelativePath).Order());
        Assert.Contains("\"pay.now\": \"Pay now\"", Read("locales/en/checkout.json"));
        Assert.Contains("\"pay.now\": \"\"", Read("locales/fr/checkout.json"));
        Assert.Equal(profile, Read("locales/en/profile.json"));
        Assert.False(_workspace.IsDirty);
        Assert.Null(_workspace.PendingDestination((await Unit("Pay now")).EntryId, "en"));
        Assert.Empty((await Save()).Receipt.Plan.Targets);
    }

    [Fact]
    public async Task DestinationsFollowUndoRedoAndRefuseOccupiedKeys()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        await Open("en");
        var checkout = Families().Single(f => f.Label == "checkout");
        Assert.Equal(EditIssueKind.KeyExists, _workspace.AddDocumentEntry(checkout, new(["title"])).Issues[0].Kind);
        Assert.True(_workspace.AddDocumentEntry(checkout, new(["new"])).Applied);
        Assert.True(_workspace.Undo());
        Assert.False(_workspace.IsDirty);
        Assert.True(_workspace.Redo());
        var id = (await _workspace.SnapshotEntriesAsync())!.Units.Single(u => u.KeyPath.Segments[0] == "new").EntryId;
        Assert.Equal("locales/en/checkout.json", _workspace.PendingDestination(id, "en"));
    }

    [Fact]
    public async Task MovingAnEntryWritesBothDocumentsAndKeepsItsIdentity()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\",\"keep\":\"Keep\"}");
        Write("locales/fr/checkout.json", "{\"title\":\"Paiement\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        Write("locales/fr/profile.json", "{\"name\":\"Nom\"}");
        await Open("en", "fr");
        var id = (await Unit("Checkout")).EntryId;
        var profile = Families().Single(f => f.Label == "profile");
        Assert.True(_workspace.MoveDocumentEntry(id, profile, new(["heading"])).Applied);
        var result = await Save();
        Assert.Equal(4, result.Receipt.Plan.Targets.Count);
        Assert.DoesNotContain("Checkout", Read("locales/en/checkout.json"));
        Assert.Equal("{\"name\":\"Nom\",\"heading\":\"Paiement\"}", Read("locales/fr/profile.json"));
        var moved = await Unit("Checkout");
        Assert.Equal(id, moved.EntryId);
        Assert.Equal(new TranslationPath(["profile"]), moved.Namespace);
    }

    [Fact]
    public async Task MoveRefusesFamiliesThatCannotPlaceEveryLanguage()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}");
        Write("locales/fr/checkout.json", "{\"title\":\"Paiement\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        await Open("en", "fr");
        var family = Families().Single(f => f.Label == "profile");
        var partial = family with { Documents = new Dictionary<string, string> { ["en"] = family.Documents["en"] } };
        var result = _workspace.MoveDocumentEntry((await Unit("Checkout")).EntryId, partial);
        Assert.False(result.Applied);
        Assert.Equal(EditIssueKind.UnknownLanguage, result.Issues[0].Kind);
    }

    [Fact]
    public async Task AddingALanguageCreatesDiscoverableDocumentsForEveryFamily()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        await Open("en");
        var result = _workspace.AddDocumentLanguage("de", Families("de"));
        Assert.True(result.Applied);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.ChangedUnits);
        var saved = await Save();
        Assert.All(saved.Receipt.Plan.Targets, t => Assert.Equal(WorkspaceDocumentSaveOperation.Create, t.Operation));
        Assert.True(Exists("locales/de/checkout.json") && Exists("locales/de/profile.json"));
        var reopened = await _services.GetRequiredService<WorkspaceSourceLoader>().LoadAsync(_root, "i18next");
        Assert.Equal(2, reopened.Units.Count(u => u.Language == "de"));
        Assert.Equal(reopened.Units.Single(u => u.Language == "en" && u.Value == "Checkout").EntryId,
            reopened.Units.Single(u => u.Language == "de" && u.KeyPath.Segments[0] == "title").EntryId);
    }

    [Fact]
    public async Task RemovingALanguageDeletesOnlyItsDocumentsAndUndoCancelsTheDeletion()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{\"title\":\"Bonjour\"}");
        Write("locales/fr/notes.txt", "unrelated");
        await Open("en", "fr");
        Assert.True(_workspace.RemoveDocumentLanguage("fr").Applied);
        Assert.True(_workspace.Undo());
        Assert.Empty((await Save()).Receipt.Plan.Targets);
        Assert.True(Exists("locales/fr/common.json"));

        Assert.True(_workspace.RemoveDocumentLanguage("fr").Applied);
        Assert.True(_workspace.IsDirty);
        var result = await Save();
        var target = Assert.Single(result.Receipt.Plan.Targets);
        Assert.Equal(WorkspaceDocumentSaveOperation.Delete, target.Operation);
        Assert.False(Exists("locales/fr/common.json"));
        Assert.True(Exists("locales/fr/notes.txt"));
        Assert.Equal("{\"title\":\"Hello\"}", Read("locales/en/common.json"));
        Assert.False(_workspace.IsDirty);
    }

    [Fact]
    public async Task RemovingAnEmptyLanguageStillDeletesItsDocuments()
    {
        Write("locales/en/common.json", "{\"title\":\"Hello\"}");
        Write("locales/fr/common.json", "{}");
        await Open("en", "fr");
        Assert.True(_workspace.RemoveDocumentLanguage("fr").Applied);
        Assert.True(_workspace.IsDirty);
        await Save();
        Assert.False(Exists("locales/fr/common.json"));
    }

    [Fact]
    public async Task RecoveryDraftsKeepPendingDestinationsAndRemovedLanguages()
    {
        Write("locales/en/checkout.json", "{\"title\":\"Checkout\"}");
        Write("locales/en/profile.json", "{\"name\":\"Name\"}");
        Write("locales/fr/profile.json", "{\"name\":\"Nom\"}");
        await Open("en", "fr");
        Assert.True(_workspace.AddDocumentEntry(Families().Single(f => f.Label == "checkout"), new(["new"]),
            new Dictionary<string, string> { ["en"] = "New" }).Applied);
        Assert.True(_workspace.RemoveDocumentLanguage("fr").Applied);
        var draft = _workspace.CaptureRecovery(_workspace.Source!);
        Assert.Equal(["fr"], draft.RemovedLanguages);

        await Open("en", "fr");
        var recovered = _workspace.ApplyRecovery(draft);
        Assert.Empty(recovered.Conflicts);
        await Save();
        Assert.Equal("{\"title\":\"Checkout\",\"new\":\"New\"}", Read("locales/en/checkout.json"));
        Assert.False(Exists("locales/fr/profile.json"));
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _services.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
