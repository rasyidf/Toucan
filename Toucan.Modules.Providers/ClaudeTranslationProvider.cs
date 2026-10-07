using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Translation with Anthropic's Claude models through the Messages API (not the OpenAI-compatible layer).
/// Options: api_key, endpoint (default https://api.anthropic.com), model (default claude-haiku-4-5-20251001), prompt.
/// </summary>
public sealed class ClaudeTranslationProvider(HttpClient? http = null) : LlmTranslationProvider(http)
{
    private const string AnthropicVersion = "2023-06-01";

    public override string Name => "Claude";

    public override ProviderDefinition? Definition { get; } = new()
    {
        Name = "Claude",
        DisplayName = "Claude (Anthropic)",
        Description = "Anthropic Claude models. Needs an API key from console.anthropic.com (separate from a Claude subscription).",
        IsBuiltIn = true,
        OptionFields = new()
        {
            ["endpoint"] = "API base URL",
            ["model"] = "Model name",
            ["prompt"] = "Custom system prompt (optional)",
        },
        SecretFields = new() { ["api_key"] = "Anthropic API key" },
        DefaultValues = new()
        {
            ["endpoint"] = "https://api.anthropic.com",
            ["model"] = "claude-haiku-4-5-20251001",
            ["prompt"] = "",
        },
    };

    protected override string ApiKeyEnvironmentVariable => "ANTHROPIC_API_KEY";
    protected override string DefaultEndpoint => "https://api.anthropic.com";
    protected override string DefaultModel => "claude-haiku-4-5-20251001";

    protected override HttpRequestMessage BuildRequest(string endpoint, string model, string apiKey, string system, string user)
    {
        var body = new
        {
            model,
            max_tokens = 4096,
            temperature = 0.3,
            system,
            messages = new[] { new { role = "user", content = user } },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        return request;
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
