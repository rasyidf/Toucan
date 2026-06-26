using System.Collections.Generic;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class PretranslationService(IEnumerable<ITranslationProvider> providers) : IPretranslationService
{
    public async Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new PretranslationResult();
        if (request == null) return result;

        var provider = (!string.IsNullOrWhiteSpace(request.Provider)
            ? providers.FirstOrDefault(p => string.Equals(p.Name, request.Provider, StringComparison.OrdinalIgnoreCase))
            : null) ?? providers.FirstOrDefault();

        if (provider == null) return result;

        var itemsToTranslate = (request.Items ?? []).ToList();

        var jobs = itemsToTranslate
            .Where(target => string.IsNullOrEmpty(target.Value) || request.Options?.Overwrite == true)
            .Select(target =>
            {
                var source = request.ContextItems?.FirstOrDefault(i => i.Namespace == target.Namespace && !string.IsNullOrEmpty(i.Value) && i.Language != target.Language)
                    ?? itemsToTranslate.FirstOrDefault(i => i.Namespace == target.Namespace && !string.IsNullOrEmpty(i.Value) && i.Language != target.Language);
                return source == null ? null : new PretranslationJob(target.Namespace, source.Value, source.Language, target.Language);
            })
            .Where(j => j != null)
            .Cast<PretranslationJob>()
            .ToList();

        if (jobs.Count == 0) return result;

        // Protect parameters before sending to provider
        var placeholderMap = new Dictionary<string, List<string>>();
        var protectedJobs = jobs.Select(j =>
        {
            var (cleaned, placeholders) = TranslationPostProcessor.ProtectParameters(j.SourceText);
            placeholderMap[$"{j.Namespace}|{j.TargetLanguage}"] = placeholders;
            return j with { SourceText = cleaned };
        }).ToList();

        var providerResults = await provider.PretranslateAsync(protectedJobs, request.Options, progress, cancellationToken).ConfigureAwait(false);

        foreach (var r in providerResults)
        {
            // Restore parameters and apply post-processing
            if (!string.IsNullOrEmpty(r.TranslatedValue))
            {
                var key = $"{r.Namespace}|{r.Language}";
                if (placeholderMap.TryGetValue(key, out var placeholders))
                    r.TranslatedValue = TranslationPostProcessor.RestoreParameters(r.TranslatedValue, placeholders);

                var originalJob = jobs.FirstOrDefault(j => j.Namespace == r.Namespace && j.TargetLanguage == r.Language);
                if (originalJob != null)
                    r.TranslatedValue = TranslationPostProcessor.PreserveUppercaseFirst(originalJob.SourceText, r.TranslatedValue);
            }

            result.Items.Add(r);

            if (!string.IsNullOrEmpty(r.TranslatedValue) && request.Options?.PreviewOnly != true)
            {
                var target = itemsToTranslate.FirstOrDefault(i => i.Namespace == r.Namespace && i.Language == r.Language);
                if (target != null && (request.Options?.Overwrite == true || string.IsNullOrEmpty(target.Value)))
                    target.Value = r.TranslatedValue;
            }
        }

        return result;
    }

    public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
        => PreTranslateAsync(new PretranslationRequest { Items = items, Options = options }, progress, cancellationToken);
}
