using Toucan.Core.Contracts;
using Toucan.Core.Services.Providers;

namespace Toucan.Core.Services;

/// <summary>The providers that ship with Toucan, in priority order (Google first: the pretranslation fallback is the first provider).</summary>
public static class BuiltInProviders
{
    public static IReadOnlyList<ITranslationProvider> Create() =>
    [
        new GoogleTranslationProvider(),
        new DeepLTranslationProvider(),
        new MicrosoftTranslationProvider(),
        new OpenAITranslationProvider(),
        new ClaudeTranslationProvider(),
        new GeminiTranslationProvider(),
        new CustomWebhookTranslationProvider(),
        new MockTranslationProvider(),
    ];
}
