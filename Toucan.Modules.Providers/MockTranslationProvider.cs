using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Providers;

public class MockTranslationProvider : ITranslationProvider
{
    public string Name => "Mock";

    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var results = new List<PretranslationItemResult>();
        var list = jobs.ToList();
        var total = list.Count;
        var processed = 0;

        foreach (var job in list)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, Succeeded = false, ErrorMessage = "Cancelled" });
                progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = "Cancelled" });
                break;
            }

            var r = new PretranslationItemResult { Namespace = job.Namespace ?? string.Empty, Language = job.TargetLanguage, Provider = Name, SourceText = job.SourceText };

            if (!string.IsNullOrEmpty(job.SourceText))
            {
                r.TranslatedValue = $"[mock/{job.TargetLanguage}] {job.SourceText}";
                r.Succeeded = true;
            }
            else
            {
                r.Succeeded = false;
                r.ErrorMessage = "No source text provided";
            }

            results.Add(r);
            processed++;
            progress?.Report(new PretranslationProgress { Completed = processed, Total = total, Message = $"Processed {processed}/{total} (mock)" });
        }

        return Task.FromResult<IEnumerable<PretranslationItemResult>>(results);
    }
}
