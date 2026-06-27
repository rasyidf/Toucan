using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Microsoft Translator (Bing) via Azure Cognitive Services REST API.
/// Requires a subscription key from Azure portal.
/// Options: api_key, region (default: global), endpoint (default: api.cognitive.microsofttranslator.com)
/// </summary>
public class MicrosoftTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient s_http = new();

    public string Name => "Microsoft";

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();

        string? apiKey = null;
        string? region = null;
        string? endpoint = null;
        if (options?.ProviderOptions != null)
        {
            options.ProviderOptions.TryGetValue("api_key", out apiKey);
            options.ProviderOptions.TryGetValue("region", out region);
            options.ProviderOptions.TryGetValue("endpoint", out endpoint);
        }

        apiKey ??= Environment.GetEnvironmentVariable("BING_TRANSLATE_API_KEY");
        region ??= Environment.GetEnvironmentVariable("BING_TRANSLATE_REGION");
        endpoint ??= "https://api.cognitive.microsofttranslator.com";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            foreach (var job in jobs)
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = !string.IsNullOrEmpty(job.SourceText), TranslatedValue = !string.IsNullOrEmpty(job.SourceText) ? $"[microsoft/{job.TargetLanguage}] {job.SourceText}" : null });
            return results;
        }

        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        // Batch by target language (Bing API supports batch in a single call)
        var byTarget = list.GroupBy(j => j.TargetLanguage);

        foreach (var group in byTarget)
        {
            var targetLang = group.Key;
            var batch = group.ToList();

            // Bing supports up to 100 texts per request
            foreach (var chunk in Chunk(batch, 100))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var sourceLang = chunk[0].SourceLanguage?.Split('-')[0];
                var url = $"{endpoint}/translate?api-version=3.0&to={Uri.EscapeDataString(targetLang.Split('-')[0])}";
                if (!string.IsNullOrEmpty(sourceLang))
                    url += $"&from={Uri.EscapeDataString(sourceLang)}";

                var body = chunk.Select(j => new { Text = j.SourceText ?? "" }).ToArray();
                var json = JsonSerializer.Serialize(body);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
                if (!string.IsNullOrEmpty(region))
                    request.Headers.Add("Ocp-Apim-Subscription-Region", region);

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
                        progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Microsoft)" });
                        continue;
                    }

                    using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var arr = doc.RootElement;

                    for (int i = 0; i < chunk.Count && i < arr.GetArrayLength(); i++)
                    {
                        var job = chunk[i];
                        var translated = arr[i].GetProperty("translations")[0].GetProperty("text").GetString();
                        results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = true, TranslatedValue = translated });
                        processed++;
                        progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Microsoft)" });
                    }
                }
                catch (Exception ex)
                {
                    foreach (var job in chunk)
                    {
                        results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                        processed++;
                    }
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Microsoft)" });
                }
            }
        }

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
