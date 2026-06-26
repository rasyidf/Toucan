using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface ITranslationProvider
{
    string Name { get; }
    Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(
        IEnumerable<PretranslationJob> jobs,
        PretranslationOptions? options = null,
        IProgress<PretranslationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
