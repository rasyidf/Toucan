using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class BulkActionService(IPretranslationService? pretranslationService = null) : IBulkActionService
{
    public async Task PreTranslateAsync(IEnumerable<TranslationItem> items, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (pretranslationService != null)
        {
            await pretranslationService.PreTranslateAsync(items, new PretranslationOptions(), progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Fallback: naive copy from first value found
        await Task.Run(() =>
        {
            var source = items.FirstOrDefault(i => !string.IsNullOrEmpty(i.Value));
            if (source == null) return;
            foreach (var item in items.Where(i => string.IsNullOrEmpty(i.Value)))
                item.Value = source.Value;
        }, cancellationToken).ConfigureAwait(false);
    }

    public string GenerateStatistics(IEnumerable<TranslationItem> items)
    {
        var byLanguage = items
            .GroupBy(i => i.Language)
            .Select(g => (Language: g.Key, Total: g.Count(), Missing: g.Count(i => string.IsNullOrEmpty(i.Value))))
            .OrderBy(o => o.Language);

        var sb = new StringBuilder("Translation statistics:\n");
        foreach (var (language, total, missing) in byLanguage)
            sb.AppendLine($"{language}: {total - missing}/{total} ({(int)((double)(total - missing) / total * 100)}% complete)");

        return sb.ToString();
    }
}
