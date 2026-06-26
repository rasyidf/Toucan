using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface IPretranslationService
{
    Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default);
}
