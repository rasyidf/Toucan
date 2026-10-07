using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

/// <summary>
/// Machine translation through AI Integration. It has no settings of its own: the AI service, model, key and the
/// Translate prompt all come from Settings → AI, and it fails every item with a clear message while AI is turned off.
/// Texts go to the model in batches of up to 20 as a JSON array and come back as a JSON array in the same order.
/// Options read from <see cref="PretranslationOptions.ProviderOptions"/>: context, formality, project_path.
/// </summary>
public sealed class AiTranslationProvider(IAiService ai) : ITranslationProvider
{
    private const int BatchSize = 20;

    public string Name => AiFeatureIds.TranslationProviderName;

    public ProviderDefinition? Definition { get; } = new()
    {
        Name = AiFeatureIds.TranslationProviderName,
        DisplayName = "AI",
        Description = "Translates with the AI service set up under Settings → AI, using the editable Translate prompt.",
        IsBuiltIn = true,
    };

    public async Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();
        var list = jobs.ToList();
        var settings = options?.ProviderOptions;
        string? Get(string key) => settings != null && settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        PretranslationItemResult Result(PretranslationJob job, string? translated, string? error) => new()
        {
            Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText,
            Succeeded = translated != null, TranslatedValue = translated, ErrorMessage = error,
        };

        if (!ai.IsFeatureEnabled(AiFeatureIds.Translate))
        {
            var reason = ai.GetStatus() is { Enabled: false } ? "AI is turned off. Turn it on under Settings → AI." : "AI translation is turned off under Settings → AI.";
            results.AddRange(list.Select(j => Result(j, null, reason)));
            return results;
        }

        // Batches in a fixed order, so a failure can report every batch that was not sent.
        var batches = list.GroupBy(j => j.TargetLanguage).SelectMany(g => g.Chunk(BatchSize)).ToList();
        var processed = 0;
        for (var b = 0; b < batches.Count; b++)
        {
            if (cancellationToken.IsCancellationRequested) return results;
            var chunk = batches[b];

            var request = new AiRequest
            {
                FeatureId = AiFeatureIds.Translate,
                Input = JsonSerializer.Serialize(chunk.Select(j => j.SourceText ?? string.Empty)),
                ProjectPath = Get("project_path"),
                Variables = new Dictionary<string, string?>
                {
                    ["source_language"] = chunk[0].SourceLanguage ?? "auto",
                    ["target_language"] = chunk[0].TargetLanguage,
                    ["context"] = Get("context"),
                    ["formality"] = Get("formality"),
                },
            };

            try
            {
                var reply = await ai.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                var translations = AiReply.ParseStringArray(reply, chunk.Length);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var t = i < translations.Count ? translations[i] : null;
                    results.Add(Result(chunk[i], t, t == null ? "No translation returned" : null));
                }
            }
            catch (AiUnavailableException ex)
            {
                // Not set up (no key, or turned off meanwhile): the remaining batches would fail the same way.
                results.AddRange(batches.Skip(b).SelectMany(c => c).Select(j => Result(j, null, ex.Message)));
                return results;
            }
            catch (AiRequestException ex)
            {
                results.AddRange(chunk.Select(j => Result(j, null, ex.Message)));
            }

            processed += chunk.Length;
            progress?.Report(new PretranslationProgress { Completed = processed, Total = list.Count, Message = $"Processed {processed}/{list.Count} (AI)" });
        }

        return results;
    }
}
