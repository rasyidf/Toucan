using Toucan.Modules;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Tests.Modules;

/// <summary>
/// Pins what the shipped composition registers, and in which order. The plugin-modularization work moves the
/// built-ins between assemblies; this snapshot must not change while it does (docs/specs/plugin-modularization).
/// </summary>
public class ModuleSnapshotTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    [Fact]
    public void SaveStrategiesKeepTheirIdsAndTheOrderUsersSee()
    {
        // The factory's list drives format pickers and `toucan list-formats`; it must not depend on which module registers first.
        using var sp = Build();
        Assert.Equal(
            ["json", "namespaced", "po", "ini", "yaml", "toml", "android-xml", "ios-strings", "xliff", "arb", "csv", "resx", "java-properties", "laravel-php"],
            sp.GetRequiredService<ITranslationStrategyFactory>().SaveStrategies.Select(s => s.FormatId));
    }

    [Fact]
    public void LoadStrategiesKeepTheirIds()
    {
        // "json" appears twice: the folder loader and the manifest loader (told apart by IManifestLoadStrategy, not by order).
        using var sp = Build();
        Assert.Equal(
            ["android-xml", "arb", "csv", "ios-strings", "java-properties", "json", "json", "laravel-php", "namespaced", "po", "resx", "toml", "xliff", "yaml"],
            sp.GetServices<ILoadStrategy>().Select(s => s.FormatId).Order(StringComparer.Ordinal));
        Assert.Single(sp.GetServices<ILoadStrategy>().OfType<IManifestLoadStrategy>());
    }

    [Fact]
    public void FormatDetectionPrioritiesAreStable()
    {
        using var sp = Build();
        var rules = sp.GetServices<ISaveStrategy>()
            .Where(s => s.Detection is not null)
            .Select(s => $"{s.FormatId}:{s.Detection!.Priority}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["android-xml:5", "arb:0", "ini:10", "ios-strings:4", "java-properties:6", "laravel-php:7", "po:2", "resx:1", "toml:9", "xliff:3", "yaml:8"],
            rules);
    }

    [Fact]
    public void ProvidersKeepTheirNamesAndOrder()
    {
        // Pretranslation falls back to the first registered provider, so Google must stay first.
        using var sp = Build();
        Assert.Equal(
            ["Google", "DeepL", "Microsoft", "AI", "Custom", "mock"],
            sp.GetServices<ITranslationProvider>().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderDefinitionsAreBuiltInAndKeepTheirSchemas()
    {
        using var sp = Build();
        var definitions = sp.GetRequiredService<ITranslationProviderRegistry>().GetAll();

        Assert.Equal(["Google", "DeepL", "Microsoft", "AI", "Custom"], definitions.Select(d => d.Name));
        Assert.All(definitions, d => Assert.True(d.IsBuiltIn));
        Assert.Equal(
            ["Google:0:1", "DeepL:1:1", "Microsoft:2:1", "AI:0:0", "Custom:2:1"],
            definitions.Select(d => $"{d.Name}:{d.OptionFields.Count}:{d.SecretFields.Count}"));
    }

    [Fact]
    public void AiServicesAndFeaturesKeepTheirIds()
    {
        // Ids are saved in ai.json and in secret keys (ai/<id>/api_key); renaming one loses the user's settings.
        using var sp = Build();
        Assert.Equal(["anthropic", "openai", "gemini"], sp.GetServices<IAiBackend>().Select(b => b.Definition.Id));
        Assert.Equal(["translate", "analyze", "clarity"], sp.GetRequiredService<IPromptLibrary>().Features.Select(f => f.Id));
    }

    [Fact]
    public void ValidationRulesKeepTheirIdsAndOrder()
    {
        using var sp = Build();
        Assert.Equal(
            ["missing-translation", "placeholder-mismatch", "duplicate-key", "untranslated-copy", "empty-value", "whitespace-mismatch"],
            sp.GetServices<IValidationRule>().Select(r => r.Id));
    }

    [Fact]
    public void FrameworkProfilesKeepTheirIdsAndOrder()
    {
        using var sp = Build();
        Assert.Equal(
            ["generic-json", "i18next", "android", "flutter-arb", "dotnet-resx", "ios-strings", "gettext", "rails-yaml"],
            sp.GetServices<IFrameworkProfile>().Select(p => p.Id));
    }
}
