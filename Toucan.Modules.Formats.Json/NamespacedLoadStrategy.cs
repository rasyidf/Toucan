using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class NamespacedLoadStrategy(JsonLoadStrategy jsonLoader) : IDocumentLoadStrategy
{
    public string FormatId => FormatIds.Namespaced;
    public IEnumerable<TranslationItem> Load(string folder) => jsonLoader.Load(folder);
    public IReadOnlyList<DocumentTranslation> LoadDocument(string content, CancellationToken cancellationToken = default) =>
        jsonLoader.LoadDocument(content, cancellationToken);
}
