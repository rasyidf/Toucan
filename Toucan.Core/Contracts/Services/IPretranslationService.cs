using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface IPretranslationService
{
    /// <summary>
    /// Pre-translate the provided request (single key, namespace, language or arbitrary items).
    /// Implementation should handle provider selection and options.
    /// </summary>
    /// <param name="request">Pretranslation request describing the target scope</param>
    /// <returns>Result summarizing successes/failures</returns>
    Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// A convenience overload to pre-translate by passing items directly.
    /// </summary>
    Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default);
}
