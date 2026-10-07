using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Services.Providers;

namespace Toucan.Modules;

public static class ProvidersModule
{
    public const string Id = "toucan.providers";

    /// <summary>
    /// Adds the built-in translation providers as the <c>toucan.providers</c> module. Google stays first:
    /// it is the fallback when pretranslation is asked for no provider (see <c>PretranslationService.DefaultProviderName</c>).
    /// </summary>
    public static IServiceCollection AddToucanProvidersModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in translation providers", "Google, DeepL, Microsoft, OpenAI, Claude, Gemini and a custom webhook", typeof(ProvidersModule).Assembly), s =>
        {
            s.AddSingleton<ITranslationProvider, GoogleTranslationProvider>();
            s.AddSingleton<ITranslationProvider, DeepLTranslationProvider>();
            s.AddSingleton<ITranslationProvider, MicrosoftTranslationProvider>();
            s.AddSingleton<ITranslationProvider, OpenAITranslationProvider>();
            s.AddSingleton<ITranslationProvider>(_ => new ClaudeTranslationProvider());
            s.AddSingleton<ITranslationProvider>(_ => new GeminiTranslationProvider());
            s.AddSingleton<ITranslationProvider, CustomWebhookTranslationProvider>();
            s.AddSingleton<ITranslationProvider, MockTranslationProvider>();
        });
}
