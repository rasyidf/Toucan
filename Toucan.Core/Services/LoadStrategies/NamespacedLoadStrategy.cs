using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies
{
    public class NamespacedLoadStrategy : ILoadStrategy
    {
        public SaveStyles Style => SaveStyles.Namespaced;

        private readonly JsonLoadStrategy _jsonLoader;

        public NamespacedLoadStrategy(JsonLoadStrategy jsonLoader)
        {
            _jsonLoader = jsonLoader;
        }

        public IEnumerable<TranslationItem> Load(string folder)
        {
            // Namespaced JSON uses the same nested representation as standard JSON; reuse implementation
            return _jsonLoader.Load(folder);
        }
    }
}
