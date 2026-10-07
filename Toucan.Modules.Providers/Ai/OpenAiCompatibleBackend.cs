using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers.Ai;

/// <summary>
/// The OpenAI Chat Completions API, which OpenAI, Azure OpenAI and local servers (Ollama, LM Studio) all speak.
/// The key is sent as a Bearer token and may be empty for a local server.
/// </summary>
public sealed class OpenAiCompatibleBackend(HttpClient? http = null) : HttpAiBackend(http)
{
    public const string Id = "openai";

    public override AiBackendDefinition Definition { get; } = new()
    {
        Id = Id,
        DisplayName = "OpenAI / compatible",
        Description = "OpenAI, Azure OpenAI, or any compatible server such as Ollama or LM Studio. Local servers need no key.",
        DefaultEndpoint = "https://api.openai.com/v1",
        DefaultModel = "gpt-4o-mini",
        ApiKeyEnvironmentVariable = "OPENAI_API_KEY",
        RequiresApiKey = false,
    };

    protected override HttpRequestMessage BuildRequest(AiCompletionRequest request, AiEndpoint endpoint)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = endpoint.Model,
            ["temperature"] = request.Temperature,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.System },
                new { role = "user", content = request.User },
            },
        };
        var message = new HttpRequestMessage(HttpMethod.Post, Url(endpoint, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(endpoint.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        return message;
    }

    protected override string? ExtractReply(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
        return choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }
}
