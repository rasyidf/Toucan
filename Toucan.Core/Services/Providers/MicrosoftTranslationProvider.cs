using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class MicrosoftTranslationProvider : ITranslationProvider
{
    public string Name => "Microsoft";

    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var list = jobs.ToList();
        var total = list.Count;
        var results = new List<PretranslationItemResult>();
        var processed = 0;

        foreach (var job in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, Succeeded = false, ErrorMessage = "Cancelled" });
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = "Cancelled" });
                break;
            }
            results.Add(new PretranslationItemResult
            {
                Namespace = job.Namespace ?? string.Empty,
                Language = job.TargetLanguage,
                Provider = Name,
                SourceText = job.SourceText,
                Succeeded = !string.IsNullOrEmpty(job.SourceText),
                TranslatedValue = !string.IsNullOrEmpty(job.SourceText) ? $"[microsoft/{job.TargetLanguage}] {job.SourceText}" : null
            });
            processed++;
            progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (Microsoft)" });
        }

        return Task.FromResult<IEnumerable<PretranslationItemResult>>(results);
    }
}
