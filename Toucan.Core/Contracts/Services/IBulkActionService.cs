using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services
{
    public interface IBulkActionService
    {
        Task PreTranslateAsync(IEnumerable<TranslationItem> items, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default);

        string GenerateStatistics(IEnumerable<TranslationItem> items);
    }
}