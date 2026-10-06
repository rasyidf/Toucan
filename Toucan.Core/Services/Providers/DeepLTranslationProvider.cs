using System.Net.Http;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class DeepLTranslationProvider : ITranslationProvider
{
    private const string ProEndpoint = "https://api.deepl.com/v2/translate";
    private const string FreeEndpoint = "https://api-free.deepl.com/v2/translate";
    private static readonly HttpClient s_shared = new();
    private readonly HttpClient _http;

    public DeepLTranslationProvider() : this(null) { }

    public DeepLTranslationProvider(HttpClient? http) => _http = http ?? s_shared;

    public string Name => "DeepL";

    public ProviderDefinition? Definition { get; } = new()
    {
        Name = "DeepL",
        DisplayName = "DeepL",
        Description = "DeepL Translator API. Free-plan keys (ending in :fx) use api-free.deepl.com automatically.",
        IsBuiltIn = true,
        OptionFields = new() { ["endpoint"] = "API endpoint URL" },
        SecretFields = new() { ["api_key"] = "DeepL API authentication key" },
        DefaultValues = new() { ["endpoint"] = "https://api.deepl.com/v2/translate" }
    };

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
        endpoint ??= Environment.GetEnvironmentVariable("DEEPL_ENDPOINT") ?? ProEndpoint;

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

        endpoint = ResolveEndpoint(apiKey, endpoint);
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
                var src = SourceLanguageCode(job.SourceLanguage);
                var tgt = TargetLanguageCode(job.TargetLanguage);

                var values = new List<KeyValuePair<string, string>>
                {
                    new("text", job.SourceText),
                    new("target_lang", tgt)
                };

                if (!string.IsNullOrEmpty(src))
                    values.Add(new KeyValuePair<string, string>("source_lang", src));

                // Wire formality if provided
                if (options?.ProviderOptions != null)
                {
                    if (options.ProviderOptions.TryGetValue("formality", out var formality) && !string.IsNullOrWhiteSpace(formality))
                        values.Add(new KeyValuePair<string, string>("formality", FormalityValue(formality)));
                    if (options.ProviderOptions.TryGetValue("context", out var context) && !string.IsNullOrWhiteSpace(context))
                        values.Add(new KeyValuePair<string, string>("context", context));
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(values) };
                request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");
                using var resp = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var error = await DescribeErrorAsync(resp, cancellationToken).ConfigureAwait(false);
                    results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage ?? string.Empty, Provider = Name, SourceText = job.SourceText, Succeeded = false, ErrorMessage = error });
                    processed++;
                    progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (DeepL)" });
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

    /// <summary>Free-plan keys end in ":fx" and only work on api-free.deepl.com; switch from the paid host so the default setting just works.</summary>
    internal static string ResolveEndpoint(string apiKey, string endpoint) =>
        apiKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase) && endpoint.Contains("//api.deepl.com", StringComparison.OrdinalIgnoreCase)
            ? FreeEndpoint
            : endpoint;

    /// <summary>DeepL accepts only the bare language for the source ("EN-US" is rejected): "en-US" → "EN". Empty means auto-detect.</summary>
    internal static string SourceLanguageCode(string? language) =>
        string.IsNullOrWhiteSpace(language) ? string.Empty : language.Split('-', '_')[0].ToUpperInvariant();

    private static readonly HashSet<string> RegionalTargets = new(StringComparer.OrdinalIgnoreCase) { "EN-US", "EN-GB", "PT-BR", "PT-PT", "ZH-HANS", "ZH-HANT" };

    /// <summary>
    /// Targets keep a region only where DeepL has distinct variants (English, Portuguese, Chinese script). "fr-FR" → "FR", "en" → "EN-US", "zh-CN" → "ZH-HANS".
    /// </summary>
    internal static string TargetLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return string.Empty;
        var normalized = language.Replace('_', '-').ToUpperInvariant();
        if (RegionalTargets.Contains(normalized)) return normalized;

        var parts = normalized.Split('-');
        var baseCode = parts[0];
        var region = parts.Length > 1 ? parts[^1] : string.Empty;
        return baseCode switch
        {
            "EN" => region == "GB" || region == "AU" || region == "NZ" || region == "ZA" || region == "IE" || region == "IN" ? "EN-GB" : "EN-US",
            "PT" => region == "BR" ? "PT-BR" : region.Length > 0 ? "PT-PT" : "PT-BR",
            "ZH" => region is "TW" or "HK" or "MO" || normalized.Contains("HANT", StringComparison.Ordinal) ? "ZH-HANT" : "ZH-HANS",
            _ => baseCode,
        };
    }

    /// <summary>
    /// Toucan's "More/Formal" and "Less/Informal" become DeepL's <c>prefer_*</c> values, which fall back to the default for languages
    /// without a formality setting instead of failing the request.
    /// </summary>
    internal static string FormalityValue(string formality) => formality.Trim().ToLowerInvariant() switch
    {
        "more" or "formal" => "prefer_more",
        "less" or "informal" => "prefer_less",
        _ => "default",
    };

    private static async Task<string> DescribeErrorAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        var status = $"HTTP {(int)resp.StatusCode} {resp.StatusCode}";
        try
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } text)
                return $"{status}: {(text.Length > 200 ? text[..200] + "…" : text)}";
        }
        catch (JsonException) { /* not JSON: status only */ }
        return status;
    }
}
