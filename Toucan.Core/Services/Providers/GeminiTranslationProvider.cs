using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Translation with Google's Gemini models through the Generative Language API (an AI Studio key works, including the free tier).
/// This is not Google Cloud Translation, which is the separate "Google" provider.
/// Options: api_key, endpoint (default https://generativelanguage.googleapis.com), model (default gemini-2.5-flash), prompt.
/// </summary>
public sealed class GeminiTranslationProvider(HttpClient? http = null) : LlmTranslationProvider(http)
{
    public override string Name => "Gemini";

    public override ProviderDefinition? Definition { get; } = new()
    {
        Name = "Gemini",
        DisplayName = "Gemini (Google AI)",
        Description = "Google Gemini models. A free API key is available from Google AI Studio (free-tier prompts may be used by Google).",
        IsBuiltIn = true,
        OptionFields = new()
        {
            ["endpoint"] = "API base URL",
            ["model"] = "Model name",
            ["prompt"] = "Custom system prompt (optional)",
        },
        SecretFields = new() { ["api_key"] = "Google AI Studio API key" },
        DefaultValues = new()
        {
            ["endpoint"] = "https://generativelanguage.googleapis.com",
            ["model"] = "gemini-2.5-flash",
            ["prompt"] = "",
        },
    };

    protected override string ApiKeyEnvironmentVariable => "GEMINI_API_KEY";
    protected override string DefaultEndpoint => "https://generativelanguage.googleapis.com";
    protected override string DefaultModel => "gemini-2.5-flash";

    protected override HttpRequestMessage BuildRequest(string endpoint, string model, string apiKey, string system, string user)
    {
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            generationConfig = new { temperature = 0.3, responseMimeType = "application/json" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        // The key goes in a header, not the URL, so it never ends up in logs or exception messages.
        request.Headers.Add("x-goog-api-key", apiKey);
        return request;
    }

    protected override string? ExtractReply(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return null;
        if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return null;
        var text = string.Concat(parts.EnumerateArray().Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null));
        return text.Length > 0 ? text : null;
    }
}
