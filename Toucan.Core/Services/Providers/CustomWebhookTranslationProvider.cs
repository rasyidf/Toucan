using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Custom webhook translation provider. Sends a POST request to a user-defined URL.
/// Options: endpoint (required), api_key (optional, sent as Bearer token), header_name (optional, custom auth header name)
///
/// Request body (JSON):
///   { "texts": ["hello", "world"], "source": "en", "target": "fr" }
///
/// Expected response (JSON):
///   { "translations": ["bonjour", "monde"] }
/// </summary>
public class CustomWebhookTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient s_http = new();

    public string Name => "Custom";

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();

        string? endpoint = null;
        string? apiKey = null;
        string? headerName = null;
        if (options?.ProviderOptions != null)
        {
            options.ProviderOptions.TryGetValue("endpoint", out endpoint);
            options.ProviderOptions.TryGetValue("api_key", out apiKey);
            options.ProviderOptions.TryGetValue("header_name", out headerName);
        }

        endpoint ??= Environment.GetEnvironmentVariable("TOUCAN_CUSTOM_ENDPOINT");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            foreach (var job in jobs)
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = "No endpoint configured" });
            return results;
        }

        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        // Batch by target language
        foreach (var group in list.GroupBy(j => j.TargetLanguage))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var targetLang = group.Key;
            var batch = group.ToList();
            var sourceLang = batch[0].SourceLanguage ?? "auto";

            var body = JsonSerializer.Serialize(new
            {
                texts = batch.Select(j => j.SourceText ?? "").ToArray(),
                source = sourceLang,
                target = targetLang
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                if (!string.IsNullOrWhiteSpace(headerName))
                    request.Headers.Add(headerName, apiKey);
                else
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            try
            {
                var resp = await s_http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = $"HTTP {resp.StatusCode}";
                    foreach (var job in batch)
                    {
                        results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = err });
                        processed++;
                    }
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Custom)" });
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var translations = new List<string?>();
                if (doc.RootElement.TryGetProperty("translations", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                        translations.Add(item.GetString());
                }

                for (int i = 0; i < batch.Count; i++)
                {
                    var job = batch[i];
                    var translated = i < translations.Count ? translations[i] : null;
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = translated != null, TranslatedValue = translated, ErrorMessage = translated == null ? "No translation in response" : null });
                    processed++;
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Custom)" });
                }
            }
            catch (Exception ex)
            {
                foreach (var job in batch)
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                    processed++;
                }
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Custom)" });
            }
        }

        return results;
    }
}
