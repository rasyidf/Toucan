using Toucan.Modules;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

/// <summary>Loads the real Toucan.TestPlugins assembly from disk through the plugin host, as an installed plugin would be.</summary>
public sealed class PluginHostTests : IDisposable
{
    private const string Ns = "Toucan.TestPlugins.";
    private static readonly string[] s_all = ["formats", "providers", "validation", "frameworks"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-plugins-" + Guid.NewGuid().ToString("N"));

    public PluginHostTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Installs a plugin folder: a manifest plus a copy of the test plugin assembly.</summary>
    private string Install(string folder, string id, string? entryType, string[]? capabilities = null, string apiVersion = "1.0", string entryAssembly = "Toucan.TestPlugins.dll")
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestPlugins")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);

        var manifest = new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = id, ["version"] = "1.0.0", ["apiVersion"] = apiVersion,
            ["entryAssembly"] = entryAssembly, ["entryType"] = entryType, ["capabilities"] = capabilities ?? s_all,
        };
        File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(manifest));
        return dir;
    }

    private ServiceProvider Build(out IPluginCatalog catalog, Func<string, bool>? isEnabled = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        var options = new PluginHostOptions { IsEnabled = isEnabled };
        options.Roots.Add(_root);
        services.AddToucanPlugins(options);

        var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        catalog = sp.GetRequiredService<IPluginCatalog>();
        return sp;
    }

    private static PluginLoadResult Single(IPluginCatalog catalog) => Assert.Single(catalog.Plugins);

    // --- a working plugin -------------------------------------------------------------------------------------

    [Fact]
    public void LoadsAPluginAndRegistersEverythingItDeclares()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out var catalog);

        var result = Single(catalog);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(["format:test-fmt", "provider:TestMt", "rule:test.rule", "framework:test-profile"], result.Registered);

        var factory = sp.GetRequiredService<ITranslationStrategyFactory>();
        Assert.NotNull(factory.GetSaveStrategy("test-fmt"));
        Assert.NotNull(factory.GetLoadStrategy("test-fmt"));
        Assert.Contains(sp.GetServices<ITranslationProvider>(), p => p.Name == "TestMt");
        Assert.Contains(sp.GetRequiredService<IValidationPipeline>().Rules, r => r.Id == "test.rule");
        Assert.Contains(sp.GetServices<IFrameworkProfile>(), p => p.Id == "test-profile");
    }

    [Fact]
    public void PluginTypesLiveInTheirOwnLoadContextButShareTheHostContracts()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out _);
        var strategy = sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt")!;

        // Isolated assembly...
        Assert.Equal("plugin:test.full", AssemblyLoadContext.GetLoadContext(strategy.GetType().Assembly)!.Name);
        // ...but the contract assemblies come from the host, so the plugin's ISaveStrategy is the host's ISaveStrategy.
        var pluginContext = AssemblyLoadContext.GetLoadContext(strategy.GetType().Assembly)!;
        Assert.DoesNotContain(pluginContext.Assemblies, a => a.GetName().Name == "Toucan.Plugins.Abstractions");
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(ISaveStrategy).Assembly));
    }

    [Fact]
    public void PluginProviderIsListedInSettingsAfterTheBuiltIns()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out _);
        var names = sp.GetRequiredService<ITranslationProviderRegistry>().GetAll().Select(d => d.Name).ToList();

        Assert.Equal(["Google", "DeepL", "Microsoft", "AI", "Custom", "TestMt"], names);
        Assert.False(sp.GetRequiredService<ITranslationProviderRegistry>().GetByName("TestMt")!.IsBuiltIn);
        // The pretranslation fallback (first provider) is still Google.
        Assert.Equal("Google", sp.GetServices<ITranslationProvider>().First().Name);
    }

    [Fact]
    public async Task PluginProviderTranslates()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out _);
        var provider = sp.GetServices<ITranslationProvider>().Single(p => p.Name == "TestMt");
        var results = (await provider.PretranslateAsync([new PretranslationJob("app.title", "Hello", "en", "fr")])).ToList();

        Assert.Equal("[testmt] Hello", Assert.Single(results).TranslatedValue);
    }

    [Fact]
    public void PluginRuleRunsInTheValidationPipeline()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out _);
        var results = sp.GetRequiredService<IValidationPipeline>().Run(
            new ValidationContext { Items = [new TranslationItem { Language = "en", Namespace = "a", Value = "forbidden" }] },
            ["test.rule"]).ToList();

        Assert.Equal("test.rule", Assert.Single(results).RuleId);
    }

    [Fact]
    public void ProjectsCanBeCreatedSavedAndReopenedInThePluginFormat()
    {
        Install("full", "test.full", Ns + "FullPlugin");
        var project = Path.Combine(_root, "my-project");

        using var sp = Build(out _);
        var service = sp.GetRequiredService<IProjectService>();

        var settings = service.CreateProject(project, ["en", "fr"], "test-fmt");
        service.Save(settings, [], [
            new TranslationItem { Language = "en", Namespace = "app.title", Value = "Hello" },
            new TranslationItem { Language = "fr", Namespace = "app.title", Value = "Bonjour" },
        ]);

        Assert.True(File.Exists(Path.Combine(project, "en.tfmt")));
        Assert.Equal("test-fmt", ProjectSettings.LoadFrom(project)!.SaveFormat);

        var reopened = service.LoadProject(project);
        Assert.Equal("test-fmt", reopened.Settings.SaveFormat);
        Assert.Contains(reopened.Translations, t => t is { Language: "fr", Namespace: "app.title", Value: "Bonjour" });
        Assert.Equal([Path.Combine(project, "fr.tfmt")], service.GetLanguageFiles(reopened.Settings, "fr"));
    }

    [Fact]
    public void FormatDetectionRecognisesThePluginFormat()
    {
        Install("full", "test.full", Ns + "FullPlugin");
        var folder = Path.Combine(_root, "detect-me");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "en.tfmt"), "a=b");

        using var sp = Build(out _);

        Assert.Equal("test-fmt", sp.GetRequiredService<FormatDetector>().Detect(folder));
    }

    [Fact]
    public void WithoutThePluginTheProjectFailsWithFormatUnavailable()
    {
        Install("full", "test.full", Ns + "FullPlugin");
        var project = Path.Combine(_root, "my-project");
        using (var sp = Build(out _))
            sp.GetRequiredService<IProjectService>().CreateProject(project, ["en"], "test-fmt");

        using var disabled = Build(out var catalog, isEnabled: _ => false);

        Assert.Equal(PluginStatus.Disabled, Single(catalog).Status);
        Assert.Throws<FormatUnavailableException>(() => disabled.GetRequiredService<IProjectService>().LoadProject(project));
    }

    // --- failures never leak -----------------------------------------------------------------------------------

    [Fact]
    public void ThrowingPluginIsReportedAndRegistersNothing()
    {
        Install("a-throwing", "test.throwing", Ns + "ThrowingPlugin");

        using var sp = Build(out var catalog);

        var result = Single(catalog);
        Assert.Equal(PluginStatus.Failed, result.Status);
        Assert.Contains("boom", result.Error);
        Assert.Contains("InvalidOperationException", result.Error);
        Assert.Null(sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt"));
    }

    [Fact]
    public void ThrowingConstructorIsReportedWithTheRealMessage()
    {
        Install("ctor", "test.ctor", Ns + "ConstructorThrowsPlugin");

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains("ctor boom", Single(catalog).Error);
    }

    [Fact]
    public void OneBadPluginDoesNotStopTheOthers()
    {
        Install("a-throwing", "test.throwing", Ns + "ThrowingPlugin");
        Install("b-full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out var catalog);

        Assert.Equal([PluginStatus.Failed, PluginStatus.Loaded], catalog.Plugins.Select(p => p.Status));
        // The failed plugin's format ID was never claimed, so the next plugin can use it.
        Assert.NotNull(sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt"));
    }

    [Fact]
    public void ResultsFollowFolderNameOrder()
    {
        Install("c", "test.c", Ns + "FullPlugin", capabilities: ["formats", "providers", "validation", "frameworks"]);
        Install("a", "test.a", Ns + "ThrowingPlugin");
        Install("b", "test.b", Ns + "ConstructorThrowsPlugin");

        using var sp = Build(out var catalog);

        Assert.Equal(["test.a", "test.b", "test.c"], catalog.Plugins.Select(p => p.DisplayId));
    }

    [Fact]
    public void TwoPluginsCannotProvideTheSameFormat()
    {
        Install("a-first", "test.first", Ns + "FullPlugin");
        Install("b-second", "test.second", Ns + "DuplicateFormatPlugin", capabilities: ["formats"]);

        using var sp = Build(out var catalog);

        Assert.Equal([PluginStatus.Loaded, PluginStatus.Failed], catalog.Plugins.Select(p => p.Status));
        Assert.Contains("already provided", catalog.Plugins[1].Error);
        Assert.Single(sp.GetServices<ISaveStrategy>(), s => s.FormatId == "test-fmt");
    }

    [Fact]
    public void RegistrationsOutsideTheDeclaredCapabilitiesAreRejected()
    {
        Install("cap", "test.cap", Ns + "UndeclaredCapabilityPlugin", capabilities: ["formats"]);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains("'validation' capability", Single(catalog).Error);
        Assert.DoesNotContain(sp.GetRequiredService<IValidationPipeline>().Rules, r => r.Id == "test.rule");
    }

    [Theory]
    [InlineData("ShadowBuiltInFormatPlugin", "formats", "Format 'json'")]
    [InlineData("ShadowBuiltInRulePlugin", "validation", "Validation rule 'missing-translation'")]
    [InlineData("ShadowBuiltInProviderPlugin", "providers", "Provider 'google'")]
    public void PluginsCannotShadowBuiltIns(string type, string capability, string expected)
    {
        Install("shadow", "test.shadow", Ns + type, capabilities: [capability]);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains(expected, Single(catalog).Error);
        Assert.Contains("already provided", Single(catalog).Error);
        // The built-in is untouched.
        Assert.IsType<Services.SaveStrategies.JsonSaveStrategy>(sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("json"));
    }

    [Theory]
    [InlineData("MismatchedFormatIdsPlugin", "formats", "differ")]
    [InlineData("BadFormatIdPlugin", "formats", "invalid")]
    [InlineData("BuiltInClaimingProviderPlugin", "providers", "built-in")]
    [InlineData("RegistersTwicePlugin", "validation", "registered twice")]
    public void MalformedRegistrationsAreRejected(string type, string capability, string expected)
    {
        Install("bad", "test.bad", Ns + type, capabilities: [capability]);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains(expected, Single(catalog).Error, StringComparison.OrdinalIgnoreCase);
    }

    // --- manifest and discovery problems -----------------------------------------------------------------------

    [Theory]
    [InlineData("2.0")]
    [InlineData("1.99")]
    [InlineData("0.5")]
    public void IncompatibleApiVersionsAreRejectedBeforeLoading(string apiVersion)
    {
        Install("old", "test.old", Ns + "FullPlugin", apiVersion: apiVersion);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Rejected, Single(catalog).Status);
        Assert.Contains("plugin API", Single(catalog).Error);
        Assert.Null(sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt"));
    }

    [Fact]
    public void InvalidManifestIsRejectedWithAllProblems()
    {
        var dir = Path.Combine(_root, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """{ "id": "Not Valid", "entryAssembly": "../x.dll" }""");

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Rejected, Single(catalog).Status);
        Assert.Null(Single(catalog).Manifest?.Name);
        Assert.Contains("'id' must use", Single(catalog).Error);
        Assert.Contains("inside the plugin folder", Single(catalog).Error);
        Assert.Equal("broken", Single(catalog).DisplayId);
    }

    [Fact]
    public void DuplicatePluginIdIsRejected()
    {
        Install("a", "test.same", Ns + "FullPlugin");
        Install("b", "test.same", Ns + "DuplicateFormatPlugin", capabilities: ["formats"]);

        using var sp = Build(out var catalog);

        Assert.Equal([PluginStatus.Loaded, PluginStatus.Rejected], catalog.Plugins.Select(p => p.Status));
        Assert.Contains("already used", catalog.Plugins[1].Error);
    }

    [Fact]
    public void MissingEntryAssemblyFails()
    {
        Install("gone", "test.gone", Ns + "FullPlugin", entryAssembly: "Missing.dll");

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains("Missing.dll", Single(catalog).Error);
    }

    [Fact]
    public void NotAManagedAssemblyFailsCleanly()
    {
        var dir = Install("junk", "test.junk", Ns + "FullPlugin", entryAssembly: "junk.dll");
        File.WriteAllText(Path.Combine(dir, "junk.dll"), "this is not an assembly");

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.False(string.IsNullOrWhiteSpace(Single(catalog).Error));
    }

    [Fact]
    public void SeveralPluginTypesWithoutEntryTypeAreAmbiguous()
    {
        Install("ambiguous", "test.ambiguous", entryType: null);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains("entryType", Single(catalog).Error);
    }

    [Theory]
    [InlineData("Toucan.TestPlugins.DoesNotExist")]
    [InlineData("Toucan.TestPlugins.NoParameterlessCtorPlugin")]
    public void UnusableEntryTypeFails(string entryType)
    {
        Install("entry", "test.entry", entryType);

        using var sp = Build(out var catalog);

        Assert.Equal(PluginStatus.Failed, Single(catalog).Status);
        Assert.Contains(entryType, Single(catalog).Error);
    }

    [Fact]
    public void DisabledPluginIsListedButNotLoaded()
    {
        Install("full", "test.full", Ns + "FullPlugin");

        using var sp = Build(out var catalog, isEnabled: id => id != "test.full");

        Assert.Equal(PluginStatus.Disabled, Single(catalog).Status);
        Assert.Equal("test.full", Single(catalog).Manifest!.Id);
        Assert.Null(Single(catalog).Registered);
        Assert.Null(sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt"));
    }

    [Fact]
    public void FoldersWithoutAManifestAndMissingRootsAreIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_root, "not-a-plugin"));
        File.WriteAllText(Path.Combine(_root, "not-a-plugin", "readme.txt"), "hi");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        var options = new PluginHostOptions();
        options.Roots.Add(_root);
        options.Roots.Add(Path.Combine(_root, "does-not-exist"));
        services.AddToucanPlugins(options);
        using var sp = services.BuildServiceProvider();

        Assert.Empty(sp.GetRequiredService<IPluginCatalog>().Plugins);
    }

    [Fact]
    public void CatalogIsRegisteredEvenWithoutPlugins()
    {
        using var sp = Build(out var catalog);
        Assert.Empty(catalog.Plugins);
    }

    [Fact]
    public void DefaultRootIsNextToTheAppSettings() =>
        Assert.EndsWith(Path.Combine("Toucan", "plugins"), PluginHostOptions.DefaultRoot());
}
