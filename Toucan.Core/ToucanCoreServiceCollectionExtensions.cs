using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Toucan.Core.Services.Frameworks;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
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
        services.AddToucanFormats();
        services.AddToucanProviders();
        services.AddToucanValidation();

        services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<ICommentPersistenceService, CommentPersistenceService>();
        return services;
    }

    /// <summary>File formats (load/save strategies), framework profiles, the strategy factory and format detection.</summary>
    public static IServiceCollection AddToucanFormats(this IServiceCollection services)
    {
        services.AddSingleton<IFileService, FileService>();

        // Save strategies
        services.AddFormat<ISaveStrategy, JsonSaveStrategy>();
        services.AddFormat<ISaveStrategy, NamespacedSaveStrategy>();
        services.AddFormat<ISaveStrategy, PoSaveStrategy>();
        services.AddFormat<ISaveStrategy, IniSaveStrategy>();
        services.AddFormat<ISaveStrategy, YamlSaveStrategy>();
        services.AddFormat<ISaveStrategy, TomlSaveStrategy>();
        services.AddFormat<ISaveStrategy, AndroidXmlSaveStrategy>();
        services.AddFormat<ISaveStrategy, IosStringsSaveStrategy>();
        services.AddFormat<ISaveStrategy, XliffSaveStrategy>();
        services.AddFormat<ISaveStrategy, ArbSaveStrategy>();
        services.AddFormat<ISaveStrategy, CsvSaveStrategy>();
        services.AddFormat<ISaveStrategy, ResxSaveStrategy>();
        services.AddFormat<ISaveStrategy, JavaPropertiesSaveStrategy>();
        services.AddFormat<ISaveStrategy, LaravelPhpSaveStrategy>();

        // Load strategies. Json before Manifest: the factory returns the first match for a format ID.
        services.AddFormat<ILoadStrategy, JsonLoadStrategy>();
        services.AddFormat<ILoadStrategy, NamespacedLoadStrategy>();
        services.AddFormat<ILoadStrategy, ManifestLoadStrategy>();
        services.AddFormat<ILoadStrategy, YamlLoadStrategy>();
        services.AddFormat<ILoadStrategy, TomlLoadStrategy>();
        services.AddFormat<ILoadStrategy, AndroidXmlLoadStrategy>();
        services.AddFormat<ILoadStrategy, IosStringsLoadStrategy>();
        services.AddFormat<ILoadStrategy, XliffLoadStrategy>();
        services.AddFormat<ILoadStrategy, ArbLoadStrategy>();
        services.AddFormat<ILoadStrategy, CsvLoadStrategy>();
        services.AddFormat<ILoadStrategy, ResxLoadStrategy>();
        services.AddFormat<ILoadStrategy, PoLoadStrategy>();
        services.AddFormat<ILoadStrategy, JavaPropertiesLoadStrategy>();
        services.AddFormat<ILoadStrategy, LaravelPhpLoadStrategy>();

        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddSingleton<FormatDetector>();

        services.AddSingleton<IFrameworkProfile, GenericJsonProfile>();
        services.AddSingleton<IFrameworkProfile, I18nextProfile>();
        services.AddSingleton<IFrameworkProfile, AndroidProfile>();
        services.AddSingleton<IFrameworkProfile, FlutterArbProfile>();
        services.AddSingleton<IFrameworkProfile, DotNetResxProfile>();
        services.AddSingleton<IFrameworkProfile, IosProfile>();
        services.AddSingleton<IFrameworkProfile, GettextProfile>();
        services.AddSingleton<IFrameworkProfile, RailsYamlProfile>();
        return services;
    }

    /// <summary>Translation providers (Google first: pretranslation falls back to the first registered provider).</summary>
    public static IServiceCollection AddToucanProviders(this IServiceCollection services)
    {
        services.AddSingleton<ITranslationProvider, Services.Providers.GoogleTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Services.Providers.DeepLTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Services.Providers.MicrosoftTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Services.Providers.OpenAITranslationProvider>();
        services.AddSingleton<ITranslationProvider, Services.Providers.CustomWebhookTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Services.Providers.MockTranslationProvider>();
        services.AddSingleton<ITranslationProviderRegistry, TranslationProviderRegistry>();
        services.AddSingleton<IPretranslationService, PretranslationService>();
        return services;
    }

    public static IServiceCollection AddToucanValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidationRule, MissingTranslationRule>();
        services.AddSingleton<IValidationRule, PlaceholderMismatchRule>();
        services.AddSingleton<IValidationRule, DuplicateKeyRule>();
        services.AddSingleton<IValidationRule, UntranslatedCopyRule>();
        services.AddSingleton<IValidationRule, EmptyValueRule>();
        services.AddSingleton<IValidationRule, WhitespaceMismatchRule>();
        services.AddSingleton<IValidationPipeline, ValidationPipeline>();
        return services;
    }

    /// <summary>Registers the concrete strategy once and forwards the interface to it (NamespacedLoadStrategy needs the concrete JsonLoadStrategy).</summary>
    private static void AddFormat<TService, TImpl>(this IServiceCollection services)
        where TService : class
        where TImpl : class, TService
    {
        services.AddSingleton<TImpl>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImpl>());
    }
}
