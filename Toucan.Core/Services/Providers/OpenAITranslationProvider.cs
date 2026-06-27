using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// OpenAI-compatible translation provider. Works with OpenAI, Azure OpenAI, or any
/// compatible endpoint (Ollama, LM Studio, etc.).
/// Options: api_key, endpoint (default: https://api.openai.com/v1), model (default: gpt-4o-mini), prompt
/// </summary>
public class OpenAITranslationProvider : ITranslationProvider
{
    private static readonly HttpClient s_http = new();

    public string Name => "OpenAI";

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();

        string? apiKey = null;
        string? endpoint = null;
        string? model = null;
        string? customPrompt = null;
        if (options?.ProviderOptions != null)
        {
            options.ProviderOptions.TryGetValue("api_key", out apiKey);
            options.ProviderOptions.TryGetValue("endpoint", out endpoint);
            options.ProviderOptions.TryGetValue("model", out model);
            options.ProviderOptions.TryGetValue("prompt", out customPrompt);
        }

        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        endpoint ??= Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1";
        model ??= Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            foreach (var job in jobs)
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = !string.IsNullOrEmpty(job.SourceText), TranslatedValue = !string.IsNullOrEmpty(job.SourceText) ? $"[openai/{job.TargetLanguage}] {job.SourceText}" : null });
            return results;
        }

        var chatUrl = $"{endpoint.TrimEnd('/')}/chat/completions";
        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        // Batch by target language to reuse system prompt
        var byTarget = list.GroupBy(j => j.TargetLanguage);

        foreach (var group in byTarget)
        {
            var targetLang = group.Key;
            var batch = group.ToList();

            // Send up to 20 texts per request as a JSON array for efficiency
            foreach (var chunk in Chunk(batch, 20))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var texts = chunk.Select(j => j.SourceText ?? "").ToList();
                var sourceLang = chunk[0].SourceLanguage ?? "auto";

                var systemMsg = customPrompt ?? $"You are a professional translator. Translate the following texts from {sourceLang} to {targetLang}. Return ONLY a JSON array of translated strings, in the same order. Do not add explanations.";

                var userContent = JsonSerializer.Serialize(texts);
                var body = new
                {
                    model,
                    messages = new object[]
                    {
                        new { role = "system", content = systemMsg },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.3
                };

                var json = JsonSerializer.Serialize(body);
                using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                try
                {
                    var resp = await s_http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = $"HTTP {resp.StatusCode}";
                        foreach (var job in chunk)
                        {
                            results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = err });
                            processed++;
                        }
                        progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (OpenAI)" });
                        continue;
                    }

                    using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";

                    // Parse the JSON array response
                    var translations = ParseTranslationArray(reply, chunk.Count);

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var job = chunk[i];
                        var translated = i < translations.Count ? translations[i] : null;
                        results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = translated != null, TranslatedValue = translated, ErrorMessage = translated == null ? "No translation returned" : null });
                        processed++;
                        progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (OpenAI)" });
                    }
                }
                catch (Exception ex)
                {
                    foreach (var job in chunk)
                    {
                        results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                        processed++;
                    }
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (OpenAI)" });
                }
            }
        }

        return results;
    }

    private static List<string?> ParseTranslationArray(string raw, int expected)
    {
        // Strip markdown code fences if present
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(trimmed);
            if (arr != null) return arr.Cast<string?>().ToList();
        }
        catch { /* fall through */ }

        // Fallback: if single item expected, return the raw text
        if (expected == 1) return [trimmed];
        return Enumerable.Repeat<string?>(null, expected).ToList();
    }

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += size)
            chunks.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return chunks;
    }
}
