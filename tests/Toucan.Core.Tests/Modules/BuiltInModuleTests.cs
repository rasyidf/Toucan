using Toucan.Modules;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.Providers;
using Xunit;

namespace Toucan.Core.Tests.Modules;

/// <summary>The built-in module seam and the contracts that replaced order and type-name matching.</summary>
public sealed class BuiltInModuleTests : IDisposable
{
    private readonly string _pluginsRoot = Path.Combine(Path.GetTempPath(), "toucan-modules-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        return services;
    }

    [Fact]
    public void CatalogListsTheBuiltInModulesWithWhatEachRegistered()
    {
        var services = NewServices();
        var options = new PluginHostOptions();
        services.AddToucanPlugins(options);
        using var sp = services.BuildServiceProvider();

        var modules = sp.GetRequiredService<IPluginCatalog>().BuiltInModules;

        // Which assembly registers a module decides the order, so only the set is pinned.
        Assert.Equal(
            ["toucan.formats.data", "toucan.formats.json", "toucan.formats.text", "toucan.formats.xml", "toucan.frameworks", "toucan.providers", "toucan.validation"],
            modules.Select(m => m.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["save-formats:3", "load-formats:4"], modules.Single(m => m.Id == "toucan.formats.json").Registered);
        Assert.Equal(["save-formats:3", "load-formats:3"], modules.Single(m => m.Id == "toucan.formats.xml").Registered);
        Assert.Equal(["save-formats:6", "load-formats:5"], modules.Single(m => m.Id == "toucan.formats.text").Registered);
        Assert.Equal(["save-formats:2", "load-formats:2"], modules.Single(m => m.Id == "toucan.formats.data").Registered);
        Assert.Equal(["formats"], modules.Single(m => m.Id == "toucan.formats.json").Capabilities);
        // Together the families are the 14 formats Toucan ships (INI saves but has no loader).
        Assert.Equal(14, modules.Where(m => m.Id.StartsWith("toucan.formats.", StringComparison.Ordinal)).Sum(m => int.Parse(m.Registered[0].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(["profiles:8"], modules.Single(m => m.Id == "toucan.frameworks").Registered);
        Assert.Equal(["providers:8"], modules.Single(m => m.Id == "toucan.providers").Registered);
        Assert.Equal(["rules:6"], modules.Single(m => m.Id == "toucan.validation").Registered);
    }

    [Fact]
    public void EveryShippedModuleReportsTheVersionOfItsOwnAssembly()
    {
        var services = NewServices();
        services.AddToucanPlugins(new PluginHostOptions());
        using var sp = services.BuildServiceProvider();

        foreach (var module in sp.GetRequiredService<IPluginCatalog>().BuiltInModules)
        {
            Assert.True(Version.TryParse(module.Version, out _), $"{module.Id} has version '{module.Version}'");
            Assert.Equal(module.Version, ExpectedVersionOf(module.Id));
        }
    }

    [Fact]
    public void ModuleWithoutAnAssemblyHasNoVersion()
    {
        var services = NewServices();
        services.AddToucanModule(new BuiltInModule("acme.none", "None"), _ => { });
        services.AddToucanPlugins(new PluginHostOptions());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(string.Empty, sp.GetRequiredService<IPluginCatalog>().BuiltInModules.Single(m => m.Id == "acme.none").Version);
    }

    private static string ExpectedVersionOf(string moduleId)
    {
        var assembly = moduleId switch
        {
            "toucan.validation" => typeof(Toucan.Modules.ValidationModule).Assembly,
            "toucan.frameworks" => typeof(Toucan.Modules.FrameworksModule).Assembly,
            "toucan.providers" => typeof(Toucan.Modules.ProvidersModule).Assembly,
            "toucan.formats.json" => typeof(Toucan.Modules.FormatsJsonModule).Assembly,
            "toucan.formats.xml" => typeof(Toucan.Modules.FormatsXmlModule).Assembly,
            "toucan.formats.text" => typeof(Toucan.Modules.FormatsTextModule).Assembly,
            "toucan.formats.data" => typeof(Toucan.Modules.FormatsDataModule).Assembly,
            _ => throw new InvalidOperationException(moduleId),
        };
        return System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)!.InformationalVersion.Split('+')[0];
    }

    [Fact]
    public void BuiltInModulesAreNotExternalPlugins()
    {
        var services = NewServices();
        services.AddToucanPlugins(new PluginHostOptions());
        using var sp = services.BuildServiceProvider();

        Assert.Empty(sp.GetRequiredService<IPluginCatalog>().Plugins);
    }

    [Fact]
    public void RegisteringTheSameModuleTwiceFails()
    {
        var services = NewServices();
        var module = new BuiltInModule("acme.module", "Acme");
        services.AddToucanModule(module, _ => { });

        Assert.Throws<InvalidOperationException>(() => services.AddToucanModule(module, _ => { }));
    }

    [Fact]
    public void ModuleDescribesOnlyWhatItAdded()
    {
        var services = NewServices();
        services.AddToucanModule(new BuiltInModule("acme.rules", "Acme rules"), s => s.AddSingleton<IValidationRule, AcmeRule>());
        services.AddToucanPlugins(new PluginHostOptions());
        using var sp = services.BuildServiceProvider();

        var info = sp.GetRequiredService<IPluginCatalog>().BuiltInModules.Single(m => m.Id == "acme.rules");
        Assert.Equal(["rules:1"], info.Registered);
        Assert.Equal(["validation"], info.Capabilities);
    }

    [Fact]
    public void ExternalPluginCannotTakeABuiltInModuleId()
    {
        var folder = Path.Combine(_pluginsRoot, "impostor");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"),
            """{ "id": "toucan.formats.json", "name": "Impostor", "version": "1.0.0", "apiVersion": "1.0", "entryAssembly": "Impostor.dll" }""");

        var services = NewServices();
        var options = new PluginHostOptions();
        options.Roots.Add(_pluginsRoot);
        services.AddToucanPlugins(options);
        using var sp = services.BuildServiceProvider();

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Rejected, result.Status);
        Assert.Contains("already used", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryNeverReturnsTheManifestLoaderForAPlainLookup_WhateverTheOrder()
    {
        var fs = new FileService(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);
        var json = new JsonLoadStrategy(fs, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonLoadStrategy>.Instance);
        var manifest = new ManifestLoadStrategy(fs, Microsoft.Extensions.Logging.Abstractions.NullLogger<ManifestLoadStrategy>.Instance);

        // Manifest first: before this contract the factory would have returned it for "json".
        var factory = new TranslationStrategyFactory([], [manifest, json]);

        Assert.Same(json, factory.GetLoadStrategy(FormatIds.Json));
        Assert.Same(manifest, factory.GetManifestLoadStrategy());
    }

    [Fact]
    public void FactoryFallsBackToTheManifestLoaderWhenItIsTheOnlyOneForAnId()
    {
        var fs = new FileService(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);
        var manifest = new ManifestLoadStrategy(fs, Microsoft.Extensions.Logging.Abstractions.NullLogger<ManifestLoadStrategy>.Instance);

        var factory = new TranslationStrategyFactory([], [manifest]);

        Assert.Same(manifest, factory.GetLoadStrategy(FormatIds.Json));
    }

    [Fact]
    public async Task PretranslationDefaultsToGoogleWhateverTheListOrder_ElseFirst()
    {
        var mock = new MockTranslationProvider();
        var google = new GoogleTranslationProvider();
        var request = new PretranslationRequest
        {
            Items = [new TranslationItem { Namespace = "a", Language = "fr", Value = "" }],
            ContextItems = [new TranslationItem { Namespace = "a", Language = "en", Value = "Hello" }],
        };

        var withGoogleLast = new PretranslationService(new ITranslationProvider[] { mock, google });
        var withoutGoogle = new PretranslationService(new ITranslationProvider[] { mock });

        // Google needs a key and a network, so it fails the item, but the provider that was tried is what we assert.
        var viaGoogle = await withGoogleLast.PreTranslateAsync(request);
        var viaMock = await withoutGoogle.PreTranslateAsync(request);

        Assert.All(viaGoogle.Items, r => Assert.Equal("Google", r.Provider));
        Assert.All(viaMock.Items, r => Assert.Equal("Mock", r.Provider));
    }

    private sealed class AcmeRule : IValidationRule
    {
        public string Id => "acme.rule";
        public string Name => "Acme";
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;
        public IEnumerable<ValidationResult> Validate(ValidationContext context) => [];
    }
}
