using System.Net.Http;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers.Ai;

/// <summary>Shared HTTP flow for chat APIs: a subclass builds the request and pulls the reply text out of the response.</summary>
public abstract class HttpAiBackend(HttpClient? http = null) : IAiBackend
{
    private static readonly HttpClient s_shared = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _http = http ?? s_shared;

    public abstract AiBackendDefinition Definition { get; }

    protected abstract HttpRequestMessage BuildRequest(AiCompletionRequest request, AiEndpoint endpoint);

    /// <summary>The model's text reply from a successful response body, or null if it has none.</summary>
    protected abstract string? ExtractReply(JsonElement root);

    public async Task<string> CompleteAsync(AiCompletionRequest request, AiEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpoint);

        try
        {
            using var message = BuildRequest(request, endpoint);
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new AiRequestException(DescribeError(response, body));

            using var doc = JsonDocument.Parse(body);
            return ExtractReply(doc.RootElement) ?? throw new AiRequestException($"{Definition.DisplayName} returned no text.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new AiRequestException(ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiRequestException($"{Definition.DisplayName} did not answer in time.", ex);
        }
    }

    /// <summary>"HTTP 401 Unauthorized: invalid x-api-key" when the body carries an <c>error.message</c>, otherwise just the status.</summary>
    internal static string DescribeError(HttpResponseMessage response, string body)
    {
        var status = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                return $"{status}: {(text.Length > 200 ? text[..200] + "…" : text)}";
        }
        catch (JsonException) { /* not JSON: status only */ }
        return status;
    }

    protected static Uri Url(AiEndpoint endpoint, string path) => new($"{endpoint.Endpoint.TrimEnd('/')}/{path.TrimStart('/')}");
}
