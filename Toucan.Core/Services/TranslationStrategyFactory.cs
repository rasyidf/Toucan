using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class TranslationStrategyFactory(IEnumerable<ISaveStrategy> saveStrategies, IEnumerable<ILoadStrategy> loadStrategies) : ITranslationStrategyFactory
{
    public ISaveStrategy? GetSaveStrategy(string formatId) =>
        SaveStrategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));

    public ILoadStrategy? GetLoadStrategy(string formatId) =>
        loadStrategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));

    public ILoadStrategy? GetManifestLoadStrategy() =>
        loadStrategies.FirstOrDefault(s => s.GetType().Name.Contains("Manifest"))
        ?? GetLoadStrategy(FormatIds.Json);

    public IReadOnlyList<ISaveStrategy> SaveStrategies { get; } = saveStrategies.ToList();
}
