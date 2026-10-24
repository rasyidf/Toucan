using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class ResourceConfigurationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-resource-config-").FullName;
    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().AddToucanCore().AddToucanDefaults().BuildServiceProvider();
    public void Dispose() { _services.Dispose(); Directory.Delete(_root, true); }
    private ProjectLoadResult Open(string json)
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), json);
        return _services.GetRequiredService<IProjectService>().LoadProject(_root);
    }
    private ResourceConfigurationPlanner Planner => new(_services.GetRequiredService<ITranslationStrategyFactory>());
    [Fact]
    public void PreviewPreservesTypedPathsAndEmptyOpaqueDataWithoutWritingOriginals()
    {
        var loaded = Open("""{"a.b":"literal","a":{"b":"nested"},"list":["value"],"null":null}""");
        var before = File.ReadAllBytes(Path.Combine(_root, "en.json"));
        var next = ProjectConfiguration.Copy(loaded.Settings); next.ResourceOptions = new() { Indent = ResourceIndent.Space4 };
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations);
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics));
        Assert.Equal(loaded.Translations.Select(i => i.Identity!.KeyToken), plan.Entries.Select(i => i.Identity!.KeyToken));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "en.json")));
        Assert.Contains(plan.Preview, line => line.Contains("\"null\": null", StringComparison.Ordinal));
    }
    [Fact]
    public void ConversionUsesAnotherWriterAndRetainsOriginalFileAndProvenance()
    {
        var loaded = Open("{\"hello\":\"world\"}"); var old = loaded.Settings.ResourceDocuments.Single().Resource;
        var next = ProjectConfiguration.Copy(loaded.Settings);
        var converted = new ResourceDescriptor { Id = "converted", RelativePath = "converted.arb", FormatId = "arb", Language = "en" };
        next.ResourceDocuments = [new() { Resource = converted }];
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations, conversions: new Dictionary<string, string> { [converted.Id] = old.Id });
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics));
        Assert.Contains(plan.Diagnostics, d => d.Code == "resource.conversion");
        _services.GetRequiredService<IProjectService>().Save(plan.Settings, [], plan.Entries);
        Assert.True(File.Exists(Path.Combine(_root, "en.json"))); Assert.True(File.Exists(Path.Combine(_root, "converted.arb")));
        Assert.Equal("world", Assert.Single(_services.GetRequiredService<IProjectService>().LoadProject(_root).Translations).Value);
        Assert.Contains("resource-conversion:converted", plan.Settings.Compatibility.Keys);
    }
    [Fact]
    public void ConversionRefusesCollapsedLiteralAndNestedIdentities()
    {
        var loaded = Open("""{"a.b":"literal","a":{"b":"nested"}}"""); var old = loaded.Settings.ResourceDocuments.Single().Resource;
        var next = ProjectConfiguration.Copy(loaded.Settings); next.ResourceDocuments = [new() { Resource = new() { Id = "converted", RelativePath = "converted.arb", FormatId = "arb", Language = "en" } }];
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations, conversions: new Dictionary<string, string> { ["converted"] = old.Id });
        Assert.False(plan.CanApply); Assert.Contains(plan.Diagnostics, d => d.Message.Contains("collapse", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_root, "converted.arb")));
    }
    [Fact]
    public void ExternalCopyIsExplicitBoundedAndNeverWritesItsSource()
    {
        var loaded = Open("{\"hello\":\"world\"}");
        var externalFolder = Directory.CreateDirectory(Path.Combine(_root, "selected-source")).FullName;
        var external = Path.Combine(externalFolder, "fr.json"); File.WriteAllText(external, "{\"hello\":\"Bonjour\",\"opaque\":null}");
        var next = ProjectConfiguration.Copy(loaded.Settings); next.ResourceDocuments.Add(new() { Resource = new() { Id = "fr", RelativePath = "copied/fr.json", FormatId = "json", Language = "fr" } });
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations, externalCopies: new Dictionary<string, string> { ["fr"] = external });
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics)); Assert.Single(plan.ExternalHashes);
        var before = File.ReadAllBytes(external);
        _services.GetRequiredService<IProjectService>().Save(plan.Settings, [], plan.Entries);
        Assert.Equal(before, File.ReadAllBytes(external)); Assert.Contains("opaque", File.ReadAllText(Path.Combine(_root, "copied/fr.json")));
    }
    [Fact]
    public void InputEncodingReloadRequiresExplicitChoiceAndSavedWorkspace()
    {
        var loaded = Open("{\"hello\":\"café\"}"); var id = loaded.Settings.ResourceDocuments.Single().Resource.Id;
        var next = ProjectConfiguration.Copy(loaded.Settings); next.ResourceOptions = new() { InputEncoding = "ISO-8859-1" };
        Assert.False(Planner.Plan(loaded.Settings, next, loaded.Translations).CanApply);
        Assert.False(Planner.Plan(loaded.Settings, next, loaded.Translations, reloadInputs: new HashSet<string> { id }, workspaceHasEdits: true).CanApply);
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations, reloadInputs: new HashSet<string> { id });
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics)); Assert.Equal("cafÃ©", Assert.Single(plan.Entries).Value);
        Assert.Contains("café", File.ReadAllText(Path.Combine(_root, "en.json")));
    }
    [Fact]
    public void NewRailsDialectWritesTheDeclaredLocaleWrapper()
    {
        var loaded = Open("{\"hello\":\"world\"}"); var old = loaded.Settings.ResourceDocuments.Single().Resource;
        var next = ProjectConfiguration.Copy(loaded.Settings);
        next.ResourceDocuments = [new() { Resource = new() { Id = "rails", FormatId = "yaml", Dialect = "rails-yaml", Language = "fr", RelativePath = "fr.yml" } }];
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations, conversions: new Dictionary<string, string> { ["rails"] = old.Id });
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics));
        _services.GetRequiredService<IProjectService>().Save(plan.Settings, [], plan.Entries);
        var back = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        Assert.Equal("hello", Assert.Single(back.Translations).Namespace); Assert.Equal("fr", back.Translations.Single().Language);
    }
    [Fact]
    public void ArbFileMetadataChangesAndRemovalBeatCachedNativeStateOnReopen()
    {
        var file = Path.Combine(_root, "app_en.arb");
        File.WriteAllText(file, """{"hello":"Hello","@hello":{"description":"Old","custom":42,"placeholders":{"name":{"type":"String"}}}}""");
        new ProjectSettings { ProjectPath = _root, SaveFormat = "arb", Languages = ["en"], ResourceDocuments = [new() { Resource = new() { Id = "arb", RelativePath = "app_en.arb", Language = "en", FormatId = "arb" } }] }.Save();
        var service = _services.GetRequiredService<IProjectService>(); var first = service.LoadProject(_root); service.Save(first.Settings, [], first.Translations);
        File.WriteAllText(file, """{"hello":"Hello","@hello":{"description":"External","custom":43}}""");
        var second = service.LoadProject(_root); var item = Assert.Single(second.Translations);
        Assert.Equal("External", item.Comment); Assert.False(item.FormatData!.ContainsKey("arb.placeholders"));
        service.Save(second.Settings, [], second.Translations);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)); var metadata = json.RootElement.GetProperty("@hello");
        Assert.Equal(43, metadata.GetProperty("custom").GetInt32()); Assert.False(metadata.TryGetProperty("placeholders", out _));
    }
    [Fact]
    public void RetargetingKeepsTypedIdentityAndLegacyFileHintsTogetherThroughReopen()
    {
        var loaded = Open("{\"hello\":\"world\"}"); var next = ProjectConfiguration.Copy(loaded.Settings);
        var identity = loaded.Translations.Single().Identity!;
        next.ResourceDocuments.Single().Resource = next.ResourceDocuments.Single().Resource with { RelativePath = "moved/catalog.json" };
        var plan = Planner.Plan(loaded.Settings, next, loaded.Translations); Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics));
        _services.GetRequiredService<IProjectService>().Save(plan.Settings, [], plan.Entries);
        Assert.True(File.Exists(Path.Combine(_root, "en.json"))); Assert.True(File.Exists(Path.Combine(_root, "moved/catalog.json")));
        var reopened = _services.GetRequiredService<IProjectService>().LoadProject(_root); var item = Assert.Single(reopened.Translations);
        Assert.Equal(identity.ResourceId, item.Identity!.ResourceId); Assert.Equal(identity.KeyToken, item.Identity.KeyToken);
        Assert.Equal("moved/catalog.json", item.FormatData!["json.file"]); Assert.Equal("world", item.Value);
    }
    [Fact]
    public void DisplayLanguageAliasesRemainSeparateFromFileLocalesDuringConfiguration()
    {
        var service = _services.GetRequiredService<IProjectService>(); var first = Open("{\"hello\":\"world\"}");
        first.Settings.LanguageAliases = new() { ["en"] = "en-US" }; first.Settings.Save();
        var loaded = service.LoadProject(_root); Assert.Equal("en-US", loaded.Translations.Single().Language);
        var proposed = ProjectConfiguration.Copy(loaded.Settings); proposed.ResourceOptions = new() { Indent = ResourceIndent.Space4 };
        var plan = Planner.Plan(loaded.Settings, proposed, loaded.Translations);
        Assert.True(plan.CanApply, string.Join(';', plan.Diagnostics)); Assert.Equal("en-US", plan.Entries.Single().Language);
        Assert.Equal("en", plan.Settings.ResourceDocuments.Single().Resource.Language);
        service.Save(plan.Settings, [], plan.Entries); var reopened = service.LoadProject(_root);
        Assert.Equal("en-US", reopened.Translations.Single().Language); Assert.True(File.Exists(Path.Combine(_root, "en.json")));
        Assert.False(File.Exists(Path.Combine(_root, "en-US.json")));
    }
    [Fact]
    public void ExistingDestinationsAndDuplicateMappingsAreRejected()
    {
        var loaded = Open("{\"hello\":\"world\"}"); var next = ProjectConfiguration.Copy(loaded.Settings);
        File.WriteAllText(Path.Combine(_root, "other.json"), "{\"other\":\"keep\"}");
        next.ResourceDocuments[0].Resource = next.ResourceDocuments[0].Resource with { RelativePath = "other.json" };
        Assert.False(Planner.Plan(loaded.Settings, next, loaded.Translations).CanApply);
        next = ProjectConfiguration.Copy(loaded.Settings); next.ResourceDocuments.Add(new() { Resource = next.ResourceDocuments[0].Resource with { Id = "duplicate" } });
        Assert.False(Planner.Plan(loaded.Settings, next, loaded.Translations).CanApply);
        Assert.Equal("{\"other\":\"keep\"}", File.ReadAllText(Path.Combine(_root, "other.json")));
    }
}
