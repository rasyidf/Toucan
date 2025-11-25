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

    public async Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request)
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

        var providerResults = (await provider.PretranslateAsync(itemsToTranslate, request.Options).ConfigureAwait(false)).ToList();

        // Apply results back to source items when the provider returned a translated value
        foreach (var r in providerResults)
        {
            result.Items.Add(r);

            if (!string.IsNullOrEmpty(r.TranslatedValue))
            {
                var target = itemsToTranslate.FirstOrDefault(i => (i.Namespace ?? string.Empty) == r.Namespace && i.Language == r.Language);
                if (target != null)
                {
                    // honor overwrite option
                    if (request.Options?.Overwrite == true || string.IsNullOrEmpty(target.Value))
                    {
                        target.Value = r.TranslatedValue;
                    }
                }
            }
        }

        return result;
    }

    public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null)
    {
        var req = new PretranslationRequest { Items = items, Options = options };
        return PreTranslateAsync(req);
    }
}
