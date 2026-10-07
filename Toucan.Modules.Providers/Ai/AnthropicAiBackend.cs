using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers.Ai;

/// <summary>Anthropic's Messages API (not the OpenAI-compatible layer). The key goes in the <c>x-api-key</c> header.</summary>
public sealed class AnthropicAiBackend(HttpClient? http = null) : HttpAiBackend(http)
{
    public const string Id = "anthropic";
    private const string AnthropicVersion = "2023-06-01";

    public override AiBackendDefinition Definition { get; } = new()
    {
        Id = Id,
        DisplayName = "Claude (Anthropic)",
        Description = "Needs an API key from console.anthropic.com. A Claude subscription does not include API access.",
        DefaultEndpoint = "https://api.anthropic.com",
        DefaultModel = "claude-haiku-4-5-20251001",
        ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY",
    };

    protected override HttpRequestMessage BuildRequest(AiCompletionRequest request, AiEndpoint endpoint)
    {
        var body = new
        {
            model = endpoint.Model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            system = request.System,
            messages = new[] { new { role = "user", content = request.User } },
        };
        var message = new HttpRequestMessage(HttpMethod.Post, Url(endpoint, "v1/messages"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(endpoint.ApiKey)) message.Headers.Add("x-api-key", endpoint.ApiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);
        return message;
    }

    protected override string? ExtractReply(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        var text = string.Concat(content.EnumerateArray()
            .Where(block => block.TryGetProperty("type", out var t) && t.GetString() == "text")
            .Select(block => block.GetProperty("text").GetString()));
        return text.Length > 0 ? text : null;
    }
}
