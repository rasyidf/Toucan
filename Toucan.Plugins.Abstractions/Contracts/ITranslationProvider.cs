using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>
    /// Identity and configuration schema shown in provider settings. Providers without a definition work but are not
    /// listed there (e.g. the mock provider). Plugin providers should supply one.
    /// </summary>
    ProviderDefinition? Definition => null;
    Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(
        IEnumerable<PretranslationJob> jobs,
        PretranslationOptions? options = null,
        IProgress<PretranslationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
