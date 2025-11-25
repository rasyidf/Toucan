using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class PretranslationService : IPretranslationService
{
    private readonly IEnumerable<ITranslationProvider> _providers;

    public PretranslationService(IEnumerable<ITranslationProvider> providers)
    {
        _providers = providers;
    }

    public async Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var result = new PretranslationResult();

        if (request == null) return result;

        // Determine provider
        ITranslationProvider? provider = null;
        if (!string.IsNullOrWhiteSpace(request.Provider))
            provider = _providers.FirstOrDefault(p => string.Equals(p.Name, request.Provider, StringComparison.InvariantCultureIgnoreCase));

        provider ??= _providers.FirstOrDefault();

        if (provider == null)
        {
            // No provider available, return empty result
            return result;
        }

        IEnumerable<TranslationItem> toProcess = request.Items ?? Enumerable.Empty<TranslationItem>();

        // If items are not supplied, the caller may expect the service to filter by key/namespace + language.
        // The service itself is not given access to a store here, so caller should populate items when possible.

        var itemsToTranslate = toProcess.ToList();

        // Build jobs: for each target item, find a source text from ContextItems or from items list if available
        var jobs = new List<PretranslationJob>();
        foreach (var target in itemsToTranslate)
        {
            // optionally skip if target already has value and no overwrite requested
            if (!string.IsNullOrEmpty(target.Value) && !(request.Options?.Overwrite == true))
                continue;

            // find source: check ContextItems (full project) first, then the supplied Items collection
            var source = request.ContextItems?.FirstOrDefault(i => i.Namespace == target.Namespace && !string.IsNullOrEmpty(i.Value) && i.Language != target.Language);
            if (source == null)
            {
                source = itemsToTranslate.FirstOrDefault(i => i.Namespace == target.Namespace && !string.IsNullOrEmpty(i.Value) && i.Language != target.Language);
            }

            if (source == null)
                continue; // no source text found for this target

            jobs.Add(new PretranslationJob
            {
                Namespace = target.Namespace ?? string.Empty,
                SourceText = source.Value,
                SourceLanguage = source.Language,
                TargetLanguage = target.Language
            });
        }

        if (!jobs.Any())
            return result;

        var providerResults = (await provider.PretranslateAsync(jobs, request.Options, progress, cancellationToken).ConfigureAwait(false)).ToList();

        // Apply results back to source items when the provider returned a translated value
        foreach (var r in providerResults)
        {
            result.Items.Add(r);

            if (!string.IsNullOrEmpty(r.TranslatedValue))
            {
                var target = itemsToTranslate.FirstOrDefault(i => (i.Namespace ?? string.Empty) == r.Namespace && i.Language == r.Language);
                if (target != null)
                {
                    // honor overwrite option -- but in preview-only mode do not modify target items
                    if (!(request.Options?.PreviewOnly == true) && (request.Options?.Overwrite == true || string.IsNullOrEmpty(target.Value)))
                    {
                        target.Value = r.TranslatedValue;
                    }
                }
            }
        }

        return result;
    }

    public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var req = new PretranslationRequest { Items = items, Options = options };
        return PreTranslateAsync(req, progress, cancellationToken);
    }
}
