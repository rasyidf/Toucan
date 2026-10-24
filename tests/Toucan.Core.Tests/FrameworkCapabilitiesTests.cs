using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class FrameworkCapabilitiesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-m5-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    private static ServiceProvider Services() => new ServiceCollection().AddLogging().AddToucanCore().AddToucanDefaults().BuildServiceProvider();
    private string Project()
    {
        Directory.CreateDirectory(Path.Combine(_root, "extracted"));
        Directory.CreateDirectory(Path.Combine(_root, "locales"));
        File.WriteAllText(Path.Combine(_root, "extracted/en.json"), """{"hello.world":{"defaultMessage":"Hello {name}","description":{"text":"Greeting","audience":"public"},"custom":{"keep":true}}}""");
        File.WriteAllText(Path.Combine(_root, "locales/de.json"), """{"hello.world":"Hallo {name}"}""");
        var settings = ProjectSettings.CreateDefault(_root);
        settings.Framework = "formatjs"; settings.SaveFormat = "formatjs"; settings.PrimaryLanguage = "en";
        settings.ResourceDocuments = [new() { Resource = new() { Id = "source", RelativePath = "extracted/en.json", Language = "en", FormatId = "formatjs" } },
            new() { Resource = new() { Id = "target", RelativePath = "locales/de.json", Language = "de", FormatId = "json", Options = new() { Layout = ResourceLayout.Literal } } }];
        settings.Save(); return _root;
    }
    [Fact]
    public void ExtractionDescriptorsAreLiteralGeneratedAndSurviveTargetSaveAndReopen()
    {
        using var services = Services(); var folder = Project(); var project = services.GetRequiredService<IProjectService>();
        var sourceBytes = File.ReadAllBytes(Path.Combine(folder, "extracted/en.json"));
        var loaded = project.LoadProject(folder);
        Assert.Equal(2, loaded.Translations.Count);
        var source = loaded.Translations.Single(i => i.Language == "en");
        Assert.Equal("hello.world", source.Identity!.Path.Single().Property);
        Assert.False(source.Permissions.EditValues); Assert.False(source.Permissions.EditKeys);
        Assert.Contains("audience", source.Comment);
        source.Value = "Forbidden"; source.Namespace = "Forbidden"; source.Comment = "Forbidden";
        Assert.Equal("Hello {name}", source.Value); Assert.DoesNotContain("Forbidden", source.Namespace);
        var target = loaded.Translations.Single(i => i.Language == "de");
        Assert.True(target.Permissions.EditValues); target.Value = "Guten Tag {name}";
        project.Save(loaded.Settings, [], loaded.Translations);
        Assert.Equal(sourceBytes, File.ReadAllBytes(Path.Combine(folder, "extracted/en.json")));
        var reopened = project.LoadProject(folder);
        Assert.False(reopened.Translations.Single(i => i.Language == "en").Permissions.EditValues);
        Assert.Equal("Guten Tag {name}", reopened.Translations.Single(i => i.Language == "de").Value);
        Assert.Equal(ResourceOwnership.GeneratedSource, reopened.Settings.ResourceDocuments.Single(d => d.Resource.Id == "source").Resource.Ownership);
    }
    [Fact]
    public void ProfileDiscoveryRejectsCompiledTargetsInsteadOfFlatteningAstNodes()
    {
        using var services = Services(); Project(); File.Delete(Path.Combine(_root, "toucan.tproj"));
        File.WriteAllText(Path.Combine(_root, "locales/de.json"), """{"hello.world":[{"type":0,"value":"Compiled"}]}""");
        var error = Assert.Throws<CompatibilityImportException>(() => services.GetRequiredService<IProjectService>().LoadProject(_root));
        Assert.Contains("Compiled AST targets are unsupported", error.Message);
    }
    [Theory]
    [InlineData("source-value")]
    [InlineData("source-comment")]
    [InlineData("delete")]
    [InlineData("add")]
    public void WorkspaceAndSaveBoundaryRejectForbiddenEdits(string change)
    {
        using var services = Services(); var loaded = services.GetRequiredService<IProjectService>().LoadProject(Project());
        var source = loaded.Translations.Single(i => i.Language == "en");
        var edit = change switch { "source-value" => new WorkspaceEdit().SetValue(source.Namespace, "en", "Wrong"),
            "source-comment" => new WorkspaceEdit().SetComment(source.Namespace, "en", "Wrong"),
            "delete" => new WorkspaceEdit().DeleteKey(source.Namespace), _ => new WorkspaceEdit().AddKey("new") };
        var plan = WorkspaceEditPlanner.Plan(new(loaded.Translations, ["en", "de"], "en", 1, false, null), edit);
        Assert.False(plan.CanApply); Assert.NotEmpty(plan.Issues);
        // Save also rejects callers that bypass normal editing APIs.
        source.Permissions = ResourcePermissions.Editable; source.Value = "Bypassed";
        Assert.Throws<FormatException>(() => services.GetRequiredService<IProjectService>().Save(loaded.Settings, [], loaded.Translations));
        Assert.Contains("Hello {name}", File.ReadAllText(Path.Combine(_root, "extracted/en.json")));
    }
    [Fact]
    public async Task FrameworkSettingsReuseTypedSchemaAndRemainDetachedUntilApplied()
    {
        using var services = Services();
        var profile = Assert.IsAssignableFrom<IFrameworkFeatures>(services.GetServices<IFrameworkProfile>().Single(p => p.Id == "i18next"));
        var options = new FrameworkOptions(); var config = new FrameworkConfiguration(profile.Settings, options, _root);
        Assert.False((await config.SetAsync("resolveNamespaces", "yes")).IsValid);
        Assert.False((await config.SetAsync("defaultPackage", "")).IsValid);
        Assert.True((await config.SetAsync("defaultPackage", "checkout")).IsValid);
        Assert.Equal("main", options.DefaultPackage); Assert.Equal("checkout", config.GetValue<string>("defaultPackage"));
        Assert.Equal("i18next", profile.ScannerId);
    }
    [Fact]
    public void GeneratedDiscoveryAndOriginalMalformedCatalogDiagnosticsAreExplicit()
    {
        using var services = Services(); Project(); File.Delete(Path.Combine(_root, "toucan.tproj"));
        var settings = ProjectSettings.CreateDefault(_root); settings.SaveFormat = "formatjs"; settings.Languages = []; settings.Save();
        var loaded = services.GetRequiredService<IProjectService>().LoadProject(_root);
        Assert.Single(loaded.Translations); Assert.False(loaded.Translations[0].Permissions.EditValues);
        File.WriteAllText(Path.Combine(_root, "extracted/en.json"), """{"bad":{"defaultMessage":[{"type":0,"value":"AST"}]}}""");
        Assert.Throws<CompatibilityImportException>(() => services.GetRequiredService<IProjectService>().LoadProject(_root));
    }
}
