using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

public class NamespacedLoadStrategy(JsonLoadStrategy jsonLoader) : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Namespaced;
    public IEnumerable<TranslationItem> Load(string folder) => jsonLoader.Load(folder);
}
