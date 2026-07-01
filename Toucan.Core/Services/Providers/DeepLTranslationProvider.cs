using System.Net.Http;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class DeepLTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient s_http = new();

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
            // fallback — report failure, not mock translations
            foreach (var job in jobs)
            {
                results.Add(new PretranslationItemResult
                {
                    Namespace = job.Namespace ?? string.Empty,
                    Language = job.TargetLanguage ?? string.Empty,
                    Provider = Name,
                    SourceText = job.SourceText,
                    Succeeded = false,
                    TranslatedValue = null,
                    ErrorMessage = "No API key configured"
                });
            }

            return results;
        }

        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        foreach (var job in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, Succeeded = false, ErrorMessage = "Cancelled" });
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = "Cancelled" });
                break;
            }
                if (string.IsNullOrEmpty(job.SourceText))
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = "No source text" });
                    continue;
                }

            try
            {
                var src = string.IsNullOrEmpty(job.SourceLanguage) ? string.Empty : job.SourceLanguage.ToUpperInvariant();
                var tgt = job.TargetLanguage?.ToUpperInvariant() ?? string.Empty;

                var values = new List<KeyValuePair<string, string>>
                {
                    new("auth_key", apiKey),
                    new("text", job.SourceText),
                    new("target_lang", tgt)
                };

                if (!string.IsNullOrEmpty(src))
                    values.Add(new KeyValuePair<string, string>("source_lang", src));

                // Wire formality if provided
                if (options?.ProviderOptions != null)
                {
                    if (options.ProviderOptions.TryGetValue("formality", out var formality) && !string.IsNullOrWhiteSpace(formality))
                        values.Add(new KeyValuePair<string, string>("formality", formality == "formal" ? "more" : formality == "informal" ? "less" : "default"));
                    if (options.ProviderOptions.TryGetValue("context", out var context) && !string.IsNullOrWhiteSpace(context))
                        values.Add(new KeyValuePair<string, string>("context", context));
                }

                var content = new FormUrlEncodedContent(values);
                var resp = await s_http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, Succeeded = false, ErrorMessage = $"HTTP {resp.StatusCode}" });
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var translated = doc.RootElement.GetProperty("translations")[0].GetProperty("text").GetString();

                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, SourceText = job.SourceText, Succeeded = true, TranslatedValue = translated });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (DeepL)" });
            }
            catch (Exception ex)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (DeepL)" });
            }
        }

        return results;
    }
}
