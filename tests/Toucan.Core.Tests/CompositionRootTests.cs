using Toucan.Modules;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>The shared composition root used by the GUI and the CLI, and the seam plugins register into.</summary>
public class CompositionRootTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        return services;
    }

    private static ServiceProvider Build(ServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

    [Fact]
    public void CoreBuildsAndResolvesTheProjectServices()
    {
        using var sp = Build(NewServices());

        Assert.NotNull(sp.GetRequiredService<IProjectService>());
        Assert.NotNull(sp.GetRequiredService<IPretranslationService>());
        Assert.NotNull(sp.GetRequiredService<IValidationPipeline>());
        Assert.NotNull(sp.GetRequiredService<ICommentPersistenceService>());
        Assert.NotNull(sp.GetRequiredService<FormatDetector>());
    }

    [Fact]
    public void EveryBuiltInFormatHasASaveStrategyAndNoDuplicates()
    {
        using var sp = Build(NewServices());
        var factory = sp.GetRequiredService<ITranslationStrategyFactory>();

        var ids = factory.SaveStrategies.Select(s => s.FormatId).ToList();
        Assert.Equal(Enum.GetValues<SaveStyles>().Select(FormatIds.FromStyle).Order(), ids.Order());
        Assert.Equal(ids.Count, ids.Distinct(FormatIds.Comparer).Count());
    }

    [Fact]
    public void EveryBuiltInFormatExceptIniHasALoader()
    {
        using var sp = Build(NewServices());
        var factory = sp.GetRequiredService<ITranslationStrategyFactory>();

        foreach (var style in Enum.GetValues<SaveStyles>().Where(s => s != SaveStyles.Adb))
            Assert.NotNull(factory.GetLoadStrategy(FormatIds.FromStyle(style)));
    }

    [Fact]
    public void StrategyInterfaceForwardsToTheSingleConcreteInstance()
    {
        using var sp = Build(NewServices());

        Assert.Same(sp.GetRequiredService<Services.LoadStrategies.JsonLoadStrategy>(), sp.GetServices<ILoadStrategy>().First(l => l.FormatId == FormatIds.Json));
    }

    [Fact]
    public void ProviderRegistryListsTheShippedProvidersWithoutMock()
    {
        using var sp = Build(NewServices());
        var registry = sp.GetRequiredService<ITranslationProviderRegistry>();

        Assert.Equal(["Google", "DeepL", "Microsoft", "AI", "Custom"], registry.GetAll().Select(d => d.Name));
        Assert.All(registry.GetAll(), d => Assert.True(d.IsBuiltIn));
        Assert.Equal("https://api.deepl.com/v2/translate", registry.GetByName("deepl")!.DefaultValues["endpoint"]);
        Assert.Null(registry.GetByName("mock"));
        // The mock provider is still registered and usable (CLI and tests rely on it).
        Assert.Contains(sp.GetServices<ITranslationProvider>(), p => p.Name.Equals("mock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PluginRegistrationsAddedAfterCoreAreUsed()
    {
        var services = NewServices();
        services.AddSingleton<ITranslationProvider, PluginProvider>();
        services.AddSingleton<IValidationRule, PluginRule>();
        services.AddSingleton<ISaveStrategy, PluginFormat>();
        services.AddSingleton<ILoadStrategy, PluginFormat>();
        using var sp = Build(services);

        Assert.Equal("Acme", sp.GetRequiredService<ITranslationProviderRegistry>().GetAll().Last().Name);
        Assert.Contains(sp.GetRequiredService<IValidationPipeline>().Rules, r => r.Id == "acme.rule");
        var factory = sp.GetRequiredService<ITranslationStrategyFactory>();
        Assert.NotNull(factory.GetSaveStrategy("acme"));
        Assert.NotNull(factory.GetLoadStrategy("acme"));
    }

    private sealed class PluginProvider : ITranslationProvider
    {
        public string Name => "Acme";
        public ProviderDefinition? Definition { get; } = new() { Name = "Acme", DisplayName = "Acme MT" };
        public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<PretranslationItemResult>>([]);
    }

    private sealed class PluginRule : IValidationRule
    {
        public string Id => "acme.rule";
        public string Name => "Acme rule";
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;
        public IEnumerable<ValidationResult> Validate(ValidationContext context) => [];
    }

    private sealed class PluginFormat : ISaveStrategy, ILoadStrategy
    {
        public string FormatId => "acme";
        public string DefaultFilePath(string language) => $"{language}.acme";
        public IEnumerable<TranslationItem> Load(string folder) => [];
        public void Save(string path, SaveContext context) { }
        public Task SaveAsync(string path, SaveContext context) => Task.CompletedTask;
    }
}
