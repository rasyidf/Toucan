using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// LLM-powered translation quality analyzer.
/// Sends source text + translation + application context to an OpenAI-compatible API.
/// The LLM evaluates whether the translation uses correct domain-specific terminology.
/// </summary>
public class TranslationAnalyzerService : ITranslationAnalyzer
{
    private static readonly HttpClient s_http = new();

    public async Task<IEnumerable<AnalysisResult>> AnalyzeAsync(AnalysisRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<AnalysisResult>();

        string? apiKey = null;
        string? endpoint = null;
        string? model = null;
        request.ProviderOptions?.TryGetValue("api_key", out apiKey);
        request.ProviderOptions?.TryGetValue("endpoint", out endpoint);
        request.ProviderOptions?.TryGetValue("model", out model);

        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        endpoint ??= Environment.GetEnvironmentVariable("OPENAI_ENDPOINT") ?? "https://api.openai.com/v1";
        model ??= Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(apiKey))
            return results; // Can't analyze without API key

        var items = request.Items.ToList();
        var total = items.Count;
        var processed = 0;

        // Process in batches of 10 to keep prompt size manageable
        foreach (var chunk in Chunk(items, 10))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var systemPrompt = BuildSystemPrompt(request);
            var userContent = BuildUserContent(chunk, request.Glossary);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/chat/completions");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpReq.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                temperature = 0.2
            }), Encoding.UTF8, "application/json");

            try
            {
                var resp = await s_http.SendAsync(httpReq, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    processed += chunk.Count;
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"HTTP {resp.StatusCode}" });
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "[]";

                var parsed = ParseAnalysisResponse(reply, chunk);
                results.AddRange(parsed);
                processed += chunk.Count;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Analyzed {processed}/{total}" });
            }
            catch (Exception ex)
            {
                processed += chunk.Count;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = ex.Message });
            }
        }

        return results;
    }

    private static string BuildSystemPrompt(AnalysisRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a professional translation quality reviewer.");
        sb.AppendLine($"Application context: {request.ApplicationContext}");
        sb.AppendLine($"Source language: {request.SourceLanguage}");
        sb.AppendLine();
        sb.AppendLine("Your task: Check each translation for domain-specific terminology errors.");
        sb.AppendLine("A word can have different meanings in different domains.");
        sb.AppendLine("Example: 'Hold' in banking means freezing funds, not physically holding something.");
        sb.AppendLine();
        sb.AppendLine("For each item, respond ONLY if there's an issue. Return a JSON array of objects:");
        sb.AppendLine("[{\"index\": 0, \"severity\": \"error|warning|suggestion\", \"issue\": \"description\", \"suggestion\": \"corrected text\", \"confidence\": 0.9}]");
        sb.AppendLine("Return [] if all translations are correct. Do not explain — only return the JSON array.");

        if (request.Glossary is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Domain glossary (preferred terms):");
            foreach (var (term, translations) in request.Glossary)
                sb.AppendLine($"  {term} → {string.Join(", ", translations.Select(kv => $"{kv.Key}: {kv.Value}"))}");
        }

        return sb.ToString();
    }

    private static string BuildUserContent(List<AnalysisItem> items, Dictionary<string, Dictionary<string, string>>? glossary)
    {
        var sb = new StringBuilder("Translations to review:\n");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine($"[{i}] key=\"{item.Namespace}\" ({item.TargetLanguage})");
            sb.AppendLine($"    source: {item.SourceText}");
            sb.AppendLine($"    translation: {item.TranslatedText}");
        }
        return sb.ToString();
    }

    private static List<AnalysisResult> ParseAnalysisResponse(string raw, List<AnalysisItem> chunk)
    {
        var results = new List<AnalysisResult>();
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```")) { var nl = trimmed.IndexOf('\n'); if (nl > 0) trimmed = trimmed[(nl + 1)..]; if (trimmed.EndsWith("```")) trimmed = trimmed[..^3]; trimmed = trimmed.Trim(); }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var idx = el.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : -1;
                if (idx < 0 || idx >= chunk.Count) continue;

                var item = chunk[idx];
                var severity = (el.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() : "warning") switch
                {
                    "error" => AnalysisSeverity.Error,
                    "suggestion" => AnalysisSeverity.Suggestion,
                    _ => AnalysisSeverity.Warning
                };

                results.Add(new AnalysisResult
                {
                    Namespace = item.Namespace,
                    TargetLanguage = item.TargetLanguage,
                    Severity = severity,
                    Issue = el.TryGetProperty("issue", out var issEl) ? issEl.GetString() ?? "" : "",
                    SuggestedFix = el.TryGetProperty("suggestion", out var sugEl) ? sugEl.GetString() : null,
                    Confidence = el.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.7
                });
            }
        }
        catch { /* LLM returned non-JSON, skip */ }

        return results;
    }

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += size)
            chunks.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return chunks;
    }
}
