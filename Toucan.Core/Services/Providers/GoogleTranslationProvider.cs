using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class GoogleTranslationProvider : ITranslationProvider
{
    private static readonly HttpClient s_http = new();

    public string Name => "Google";

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();
        string? apiKey = null;
        if (options?.ProviderOptions != null && options.ProviderOptions.TryGetValue("api_key", out var _api))
            apiKey = _api;
        apiKey ??= Environment.GetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY");

        // Http fallback: if no API key available, report failure so app doesn't save mock translations
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            foreach (var job in jobs)
            {
                results.Add(new PretranslationItemResult
                {
                    Namespace = job.Namespace ?? string.Empty,
                    Language = job.TargetLanguage,
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

        // Translate each job individually (simple implementation). Could batch by target language later.
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
                var targetLang = job.TargetLanguage;
                var sourceLang = string.IsNullOrEmpty(job.SourceLanguage) ? null : job.SourceLanguage;

                var url = "https://translation.googleapis.com/language/translate/v2?key=" + Uri.EscapeDataString(apiKey);

                var values = new List<KeyValuePair<string, string>>();
                values.Add(new KeyValuePair<string, string>("q", job.SourceText));
                values.Add(new KeyValuePair<string, string>("target", targetLang));
                if (!string.IsNullOrEmpty(sourceLang))
                    values.Add(new KeyValuePair<string, string>("source", sourceLang));

                var content = new FormUrlEncodedContent(values);
                var resp = await s_http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, Succeeded = false, ErrorMessage = $"HTTP {resp.StatusCode}" });
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var translated = WebUtility.HtmlDecode(doc.RootElement.GetProperty("data").GetProperty("translations")[0].GetProperty("translatedText").GetString());

                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = true, TranslatedValue = translated });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Google)" });
            }
            catch (Exception ex)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = ex.Message });
                processed++;
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Google)" });
            }
        }

        return results;
    }
}
