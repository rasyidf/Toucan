using System.Net.Http;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class DeepLTranslationProvider : ITranslationProvider
{
    public string Name => "DeepL";

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();

        string? apiKey = null;
        string? endpoint = null;
        if (options?.ProviderOptions != null)
        {
            options.ProviderOptions.TryGetValue("api_key", out apiKey);
            options.ProviderOptions.TryGetValue("endpoint", out endpoint);
        }

        apiKey ??= Environment.GetEnvironmentVariable("DEEPL_API_KEY");
        endpoint ??= Environment.GetEnvironmentVariable("DEEPL_ENDPOINT") ?? "https://api.deepl.com/v2/translate";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // fallback to mock style
            foreach (var job in jobs)
            {
                results.Add(new PretranslationItemResult
                {
                    Namespace = job.Namespace ?? string.Empty,
                    Language = job.TargetLanguage,
                    Provider = Name,
                    SourceText = job.SourceText,
                    Succeeded = !string.IsNullOrEmpty(job.SourceText),
                    TranslatedValue = !string.IsNullOrEmpty(job.SourceText) ? $"[deepl/{job.TargetLanguage}] {job.SourceText}" : null
                });
            }

            return results;
        }

        using var client = new HttpClient();
        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        foreach (var job in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, Succeeded = false, ErrorMessage = "Cancelled" });
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = "Cancelled" });
                break;
            }
                if (string.IsNullOrEmpty(job.SourceText))
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = "No source text" });
                    continue;
                }

            try
            {
                var src = job.SourceLanguage?.Split('-')[0].ToUpperInvariant() ?? string.Empty;
                var tgt = job.TargetLanguage?.Split('-')[0].ToUpperInvariant() ?? string.Empty;

                var values = new List<KeyValuePair<string, string>>
                {
                    new("auth_key", apiKey),
                    new("text", job.SourceText),
                    new("target_lang", tgt)
                };

                if (!string.IsNullOrEmpty(src))
                    values.Add(new KeyValuePair<string, string>("source_lang", src));

                var content = new FormUrlEncodedContent(values);
                var resp = await client.PostAsync(endpoint, content).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, Succeeded = false, ErrorMessage = $"HTTP {resp.StatusCode}" });
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                var translated = doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();

                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = true, TranslatedValue = translated });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (DeepL)" });
            }
            catch (Exception ex)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (DeepL)" });
            }
        }

        return results;
    }
}
