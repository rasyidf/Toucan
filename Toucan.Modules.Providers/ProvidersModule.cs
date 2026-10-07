using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Services.Providers;
using Toucan.Core.Services.Providers.Ai;

namespace Toucan.Modules;

public static class ProvidersModule
{
    public const string Id = "toucan.providers";

    /// <summary>
    /// Adds the built-in translation providers and AI services as the <c>toucan.providers</c> module. Google stays first:
    /// it is the fallback when pretranslation is asked for no provider (see <c>PretranslationService.DefaultProviderName</c>).
    /// Claude, OpenAI and Gemini are AI services (<see cref="IAiBackend"/>), not translation providers: machine translation
    /// reaches them through the "AI" provider, which uses AI Integration.
    /// </summary>
    public static IServiceCollection AddToucanProvidersModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in translation providers", "Google, DeepL, Microsoft, AI, a custom webhook, and the Claude, OpenAI and Gemini AI services", typeof(ProvidersModule).Assembly), s =>
        {
            s.AddSingleton<ITranslationProvider, GoogleTranslationProvider>();
            s.AddSingleton<ITranslationProvider, DeepLTranslationProvider>();
            s.AddSingleton<ITranslationProvider, MicrosoftTranslationProvider>();
            s.AddSingleton<ITranslationProvider, AiTranslationProvider>();
            s.AddSingleton<ITranslationProvider, CustomWebhookTranslationProvider>();
            s.AddSingleton<ITranslationProvider, MockTranslationProvider>();

            s.AddSingleton<IAiBackend>(_ => new AnthropicAiBackend());
            s.AddSingleton<IAiBackend>(_ => new OpenAiCompatibleBackend());
            s.AddSingleton<IAiBackend>(_ => new GeminiAiBackend());
        });
}
