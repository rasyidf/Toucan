using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface ITranslationProvider
{
    /// <summary>
    /// Unique provider name, e.g. "DeepL", "Google", "Mock"
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Translate the supplied items and return per-item results.
    /// </summary>
    /// <summary>
    /// Accepts translation jobs where source text and source language are provided alongside target language and namespace.
    /// </summary>
    Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default);
}
