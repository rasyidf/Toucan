using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class TranslationStrategyFactory(IEnumerable<ISaveStrategy> saveStrategies, IEnumerable<ILoadStrategy> loadStrategies) : ITranslationStrategyFactory
{
    public ISaveStrategy? GetSaveStrategy(SaveStyles style) =>
        saveStrategies.FirstOrDefault(s => s.Style == style);

    public ILoadStrategy? GetLoadStrategy(SaveStyles style) =>
        loadStrategies.FirstOrDefault(s => s.Style == style);

    public ILoadStrategy? GetManifestLoadStrategy() =>
        loadStrategies.FirstOrDefault(s => s.GetType().Name.Contains("Manifest"))
        ?? GetLoadStrategy(SaveStyles.Json);
}
