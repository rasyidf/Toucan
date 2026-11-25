using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class GenericJsonWorkspaceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-generic-workspace-").FullName;
    private readonly ServiceProvider _services;

    public GenericJsonWorkspaceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private string PathFor(string language) => Path.Combine(_root, language + ".json");
    private WorkspaceService Open(string content, ResourceUnicodeEscapes? escapes = null)
    {
        File.WriteAllText(PathFor("en"), content);
        File.WriteAllText(PathFor("fr"), content);
        new ProjectSettings { ProjectPath = _root, Framework = "generic-json", SaveFormat = "json",
            PrimaryLanguage = "en", Languages = ["en", "fr"], ResourceOptions = new() { UnicodeEscapes = escapes } }.Save();
        var loaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        var workspace = _services.GetRequiredService<WorkspaceService>();
        workspace.Open(loaded.Settings, loaded.Source);
        return workspace;
    }

    private Task<WorkspaceDocumentSaveResult> Save(WorkspaceService workspace) =>
        _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(workspace.Source!);

    [Fact]
    public async Task LanguageFilesShareIdentityAndSupportEditsAndNewLanguages()
    {
        var workspace = Open("{\"title\":\"Hello\"}");
        var before = (await workspace.SnapshotEntriesAsync())!;
        Assert.Single(before.Units.Select(u => u.EntryId).Distinct());
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().SetValue("title", "fr", "Bonjour")).Applied);
        var families = _services.GetRequiredService<WorkspaceDocumentRouter>().DestinationFamilies(workspace.Source!, ["en", "fr", "de"]);
        var family = Assert.Single(families);
        Assert.Equal("de.json", family.Documents["de"]);
        Assert.True(workspace.AddDocumentLanguage("de", families).Applied);
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().SetValue("title", "de", "Hallo")).Applied);
        await Save(workspace);
        var reopened = await _services.GetRequiredService<WorkspaceSourceLoader>().LoadAsync(_root, "generic-json");
        Assert.Single(reopened.Units.Select(u => u.EntryId).Distinct());
        Assert.Equal(3, reopened.Units.Count);
        using var json = JsonDocument.Parse(File.ReadAllText(PathFor("de")));
        Assert.Equal("Hallo", json.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(null, "Caf\\u00E9")]
    [InlineData(ResourceUnicodeEscapes.Preserve, "Caf\\u00E9")]
    [InlineData(ResourceUnicodeEscapes.Minimal, "Café")]
    public async Task NewDocumentsKeepAlwaysUnlessMinimalIsSelected(ResourceUnicodeEscapes? policy, string expected)
    {
        var workspace = Open("{\"title\":\"Hello\"}", policy);
        var families = _services.GetRequiredService<WorkspaceDocumentRouter>().DestinationFamilies(workspace.Source!, ["de"]);
        Assert.True(workspace.AddDocumentLanguage("de", families).Applied);
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().SetValue("title", "de", "Café")).Applied);
        await Save(workspace);
        Assert.Contains(expected, File.ReadAllText(PathFor("de")));
    }

    [Theory]
    [InlineData("[\"Hello\",null,1e+02]", "0")]
    [InlineData("[{\"title\":\"Hello\"},null]", "0.title")]
    public async Task ArrayRootsOpenAndValueEditsRetainTheirOtherTokens(string content, string key)
    {
        var workspace = Open(content);
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().SetValue(key, "en", "Changed")).Applied);
        await Save(workspace);
        Assert.Equal(content.Replace("Hello", "Changed", StringComparison.Ordinal), File.ReadAllText(PathFor("en")));
        Assert.Equal(content, File.ReadAllText(PathFor("fr")));
    }

    [Fact]
    public async Task RenamingAnObjectPropertyInsideARootArrayRetainsTheArray()
    {
        var workspace = Open("[{\"title\":\"Hello\"},null]");
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        var id = snapshot.Units.First().EntryId;
        Assert.True(workspace.ApplyEntries("editor", new(snapshot.SessionId, snapshot.Revision,
            [new RenameEntry(id, TranslationPath.Empty, new(["0", "renamed"]))])).Applied);
        await Save(workspace);
        using var json = JsonDocument.Parse(File.ReadAllText(PathFor("en")));
        Assert.Equal("Hello", json.RootElement[0].GetProperty("renamed").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement[1].ValueKind);
    }

    [Theory]
    [InlineData(ResourceUnicodeEscapes.Minimal, "Hello", "Café 👍")]
    [InlineData(ResourceUnicodeEscapes.Always, "Hello", "Caf\\u00E9 \\uD83D\\uDC4D")]
    [InlineData(ResourceUnicodeEscapes.Preserve, "Café", "Café 👍")]
    [InlineData(ResourceUnicodeEscapes.Preserve, "Caf\\u00E9", "Caf\\u00E9 \\uD83D\\uDC4D")]
    public async Task ValueAndStructuralEditsHonorTheCapturedUnicodePolicy(ResourceUnicodeEscapes policy, string original, string expected)
    {
        var workspace = Open("{\"title\":\"" + original + "\"}", policy);
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().SetValue("title", "en", "Café 👍")).Applied);
        await Save(workspace);
        Assert.Contains("\"title\":\"" + expected + "\"", File.ReadAllText(PathFor("en")));
        Assert.True(workspace.Apply("cli", new WorkspaceEdit().RenameKey("title", "renamed")).Applied);
        await Save(workspace);
        Assert.Contains(expected, File.ReadAllText(PathFor("en")));
        var reloaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        Assert.Equal("Café 👍", reloaded.Translations.Single(t => t.Language == "en").Value);
    }

    public void Dispose() { _services.Dispose(); Directory.Delete(_root, recursive: true); }
}
