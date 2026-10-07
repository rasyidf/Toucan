using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Services.Ai;
using Toucan.Core.Services.Validation;

namespace Toucan.Core;

/// <summary>
/// Shared composition root for everything that does not depend on a UI host. The GUI and the CLI both call
/// <see cref="AddToucanCore"/>; plugins register their formats, providers and rules into the same collection
/// before the container is built. Hosts must register logging (<c>ILogger&lt;T&gt;</c>) themselves.
/// </summary>
public static class ToucanCoreServiceCollectionExtensions
{
    public static IServiceCollection AddToucanCore(this IServiceCollection services)
    {
        // Core registers the shared services only. Formats, providers, rules and profiles are built-in modules (see AddToucanDefaults).
        services.AddToucanFormats();
        services.AddToucanProviders();
        services.AddToucanValidation();
        services.AddToucanAi();

        services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ICommentPersistenceService, CommentPersistenceService>();
        return services;
    }

    /// <summary>File access, the strategy factory and format detection. The formats themselves come from the <c>Toucan.Modules.Formats.*</c> modules, plugins add more.</summary>
    public static IServiceCollection AddToucanFormats(this IServiceCollection services)
    {
        services.AddSingleton<IFileService, FileService>();

        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddSingleton<FormatDetector>();
        return services;
    }

    /// <summary>The provider registry and pretranslation service. The providers themselves come from <c>Toucan.Modules.Providers</c>, plugins add more.</summary>
    public static IServiceCollection AddToucanProviders(this IServiceCollection services)
    {
        services.AddSingleton<ITranslationProviderRegistry, TranslationProviderRegistry>();
        services.AddSingleton<IPretranslationService, PretranslationService>();
        return services;
    }

    /// <summary>
    /// The secret store, AI Integration (settings, prompts, the app-wide switch) and the AI features built on it.
    /// The AI services themselves (Claude, OpenAI, Gemini) come from <c>Toucan.Modules.Providers</c>. Registered with
    /// TryAdd, so a host can supply its own store first (tests use temporary folders).
    /// </summary>
    public static IServiceCollection AddToucanAi(this IServiceCollection services)
    {
        services.TryAddSingleton<ISecureStorageService, SecureStorageService>();
        services.TryAddSingleton<ISecretService>(sp => new SecretService(sp.GetRequiredService<ISecureStorageService>()));

        foreach (var feature in BuiltInAiFeatures.All) services.AddSingleton(feature);
        services.TryAddSingleton<IPromptLibrary>(sp => new PromptLibrary(sp.GetServices<AiFeatureDefinition>(), PromptLibrary.DefaultUserFolder));
        services.TryAddSingleton<IAiSettingsStore>(sp => new AiSettingsStore(AiSettingsStore.DefaultPath, () => LegacyAiMigration.Migrate(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "providers.json"),
            sp.GetRequiredService<ISecureStorageService>(),
            sp.GetRequiredService<ISecretService>(),
            AppOptions.LoadFromDisk().LastProvider,
            Path.Combine(PromptLibrary.DefaultUserFolder, AiFeatureIds.Translate + ".md"))));

        services.TryAddSingleton<AiService>();
        services.TryAddSingleton<IAiService>(sp => sp.GetRequiredService<AiService>());
        services.TryAddSingleton<ITranslationAnalyzer, TranslationAnalyzerService>();
        services.TryAddSingleton<ISourceClarityService, SourceClarityService>();
        return services;
    }

    /// <summary>The validation pipeline. The built-in rules come from <c>Toucan.Modules.Validation</c>, plugins add more.</summary>
    public static IServiceCollection AddToucanValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidationPipeline, ValidationPipeline>();
        return services;
    }
}
