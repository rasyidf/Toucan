using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services
{
    public class TranslationStrategyFactory : ITranslationStrategyFactory
    {
        private readonly IEnumerable<ISaveStrategy> _saveStrategies;
        private readonly IEnumerable<ILoadStrategy> _loadStrategies;

        public TranslationStrategyFactory(IEnumerable<ISaveStrategy> saveStrategies, IEnumerable<ILoadStrategy> loadStrategies)
        {
            _saveStrategies = saveStrategies;
            _loadStrategies = loadStrategies;
        }

        public ISaveStrategy GetSaveStrategy(SaveStyles style)
        {
            return _saveStrategies?.FirstOrDefault(s => s.Style == style);
        }

        public ILoadStrategy GetLoadStrategy(SaveStyles style)
        {
            return _loadStrategies?.FirstOrDefault(s => s.Style == style);
        }

        public ILoadStrategy GetManifestLoadStrategy()
        {
            // prefer an explicit ManifestLoadStrategy if registered; otherwise return JSON loader
            var manifest = _loadStrategies?.FirstOrDefault(s => s.GetType().Name.Contains("Manifest"));
            if (manifest != null) return manifest;
            return GetLoadStrategy(SaveStyles.Json);
        }
    }
}
