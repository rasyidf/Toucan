using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers.Ai;

/// <summary>
/// Google's Gemini models through the Generative Language API (an AI Studio key works, including the free tier).
/// This is not Google Cloud Translation, which is the separate "Google" translation provider.
/// </summary>
public sealed class GeminiAiBackend(HttpClient? http = null) : HttpAiBackend(http)
{
    public const string Id = "gemini";

    public override AiBackendDefinition Definition { get; } = new()
    {
        Id = Id,
        DisplayName = "Gemini (Google AI)",
        Description = "A free API key is available from Google AI Studio. Google may use free-tier prompts to improve its products.",
        DefaultEndpoint = "https://generativelanguage.googleapis.com",
        DefaultModel = "gemini-flash-latest",
        ApiKeyEnvironmentVariable = "GEMINI_API_KEY",
    };

    protected override HttpRequestMessage BuildRequest(AiCompletionRequest request, AiEndpoint endpoint)
    {
        object generationConfig = request.JsonResponse
            ? new { temperature = request.Temperature, responseMimeType = "application/json" }
            : new { temperature = request.Temperature };
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = request.System } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.User } } } },
            generationConfig,
        };
        var message = new HttpRequestMessage(HttpMethod.Post, Url(endpoint, $"v1beta/models/{Uri.EscapeDataString(endpoint.Model)}:generateContent"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        // The key goes in a header, not the URL, so it never ends up in logs or exception messages.
        if (!string.IsNullOrEmpty(endpoint.ApiKey)) message.Headers.Add("x-goog-api-key", endpoint.ApiKey);
        return message;
    }

    protected override string? ExtractReply(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return null;
        if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return null;
        var text = string.Concat(parts.EnumerateArray().Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null));
        return text.Length > 0 ? text : null;
    }
}
