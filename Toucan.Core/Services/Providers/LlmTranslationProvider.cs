using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Shared flow for chat-model providers (Claude, Gemini): texts go to the model in batches of up to 20 as a JSON array,
/// and the reply is read back as a JSON array in the same order. A subclass only builds the HTTP request and pulls the reply text out.
/// Options read from <see cref="PretranslationOptions.ProviderOptions"/>: api_key, endpoint, model, prompt, context, formality.
/// </summary>
public abstract class LlmTranslationProvider(HttpClient? http = null) : ITranslationProvider
{
    private const int BatchSize = 20;
    private static readonly HttpClient s_shared = new();
    private readonly HttpClient _http = http ?? s_shared;

    public abstract string Name { get; }
    public abstract ProviderDefinition? Definition { get; }

    /// <summary>Environment variable consulted when no api_key is configured.</summary>
    protected abstract string ApiKeyEnvironmentVariable { get; }
    protected abstract string DefaultEndpoint { get; }
    protected abstract string DefaultModel { get; }

    /// <summary>Builds the HTTP request for one batch. <paramref name="user"/> is the JSON array of source texts.</summary>
    protected abstract HttpRequestMessage BuildRequest(string endpoint, string model, string apiKey, string system, string user);

    /// <summary>Extracts the model's text reply from a successful response body, or null if it has none.</summary>
    protected abstract string? ExtractReply(JsonElement root);

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();
        var list = jobs.ToList();
        var settings = options?.ProviderOptions;

        string? Get(string key) => settings != null && settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        var apiKey = Get("api_key") ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var endpoint = Get("endpoint") ?? DefaultEndpoint;
        var model = Get("model") ?? DefaultModel;

        PretranslationItemResult Result(PretranslationJob job, string? translated, string? error) => new()
        {
            Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText,
            Succeeded = translated != null, TranslatedValue = translated, ErrorMessage = error,
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            results.AddRange(list.Select(j => Result(j, null, "No API key configured")));
            return results;
        }

        var processed = 0;
        foreach (var group in list.GroupBy(j => j.TargetLanguage))
        {
            foreach (var chunk in group.Chunk(BatchSize))
            {
                if (cancellationToken.IsCancellationRequested) return results;

                var system = BuildSystemPrompt(chunk[0].SourceLanguage ?? "auto", group.Key, Get("prompt"), Get("context"), Get("formality"));
                var user = JsonSerializer.Serialize(chunk.Select(j => j.SourceText ?? string.Empty));

                try
                {
                    using var request = BuildRequest(endpoint, model, apiKey, system, user);
                    using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = DescribeError(response, body);
                        results.AddRange(chunk.Select(j => Result(j, null, error)));
                    }
                    else
                    {
                        using var doc = JsonDocument.Parse(body);
                        var reply = ExtractReply(doc.RootElement);
                        var translations = reply is null ? [] : ParseTranslationArray(reply, chunk.Length);
                        for (var i = 0; i < chunk.Length; i++)
                        {
                            var t = i < translations.Count ? translations[i] : null;
                            results.Add(Result(chunk[i], t, t == null ? "No translation returned" : null));
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or TaskCanceledException)
                {
                    results.AddRange(chunk.Select(j => Result(j, null, ex.Message)));
                }

                processed += chunk.Length;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = list.Count, Message = $"Processed {processed}/{list.Count} ({Name})" });
            }
        }

        return results;
    }

    /// <summary>The prompt used when the provider's <c>prompt</c> option is empty, plus the app context and formality if set.</summary>
    internal static string BuildSystemPrompt(string source, string target, string? custom, string? context, string? formality)
    {
        var sb = new StringBuilder(string.IsNullOrWhiteSpace(custom)
            ? $"You are a professional translator for software user interfaces. Translate the following texts from {source} to {target}. " +
              "Return ONLY a JSON array of translated strings, in the same order and the same length, with no explanations and no code fences. " +
              "Keep placeholders such as {{name}}, {0}, %s, %d and :param exactly as written."
            : custom);
        if (!string.IsNullOrWhiteSpace(context)) sb.Append(" Application context: ").Append(context.Trim());
        if (!string.IsNullOrWhiteSpace(formality)) sb.Append(" Use a ").Append(formality.Trim()).Append(" register where the language distinguishes one.");
        return sb.ToString();
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
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                return $"{status}: {(text.Length > 200 ? text[..200] + "…" : text)}";
        }
        catch (JsonException) { /* not JSON: status only */ }
        return status;
    }

    internal static List<string?> ParseTranslationArray(string raw, int expected)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```", StringComparison.Ordinal)) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(trimmed);
            if (arr != null) return [.. arr];
        }
        catch (JsonException) { /* fall through */ }

        return expected == 1 ? [trimmed] : [.. Enumerable.Repeat<string?>(null, expected)];
    }
}
