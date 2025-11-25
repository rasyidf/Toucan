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
    Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null);
}
