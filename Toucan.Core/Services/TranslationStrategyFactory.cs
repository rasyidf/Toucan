using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class TranslationStrategyFactory(IEnumerable<ISaveStrategy> saveStrategies, IEnumerable<ILoadStrategy> loadStrategies) : ITranslationStrategyFactory
{
    public ISaveStrategy? GetSaveStrategy(string formatId) =>
        SaveStrategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));

    // The manifest loader shares the "json" ID with the folder loader; a plain lookup never returns it, whatever the registration order.
    public ILoadStrategy? GetLoadStrategy(string formatId) =>
        loadStrategies.FirstOrDefault(s => s is not IManifestLoadStrategy && FormatIds.Comparer.Equals(s.FormatId, formatId))
        ?? loadStrategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));

    public ILoadStrategy? GetManifestLoadStrategy() =>
        loadStrategies.OfType<IManifestLoadStrategy>().FirstOrDefault()
        ?? GetLoadStrategy(FormatIds.Json);

    /// <summary>
    /// The order users see in format pickers and <c>toucan list-formats</c>: the formats that ship with Toucan in
    /// their long-standing order, then everything else (plugins) in registration order. It does not depend on which
    /// module registered first.
    /// </summary>
    public IReadOnlyList<ISaveStrategy> SaveStrategies { get; } = [.. saveStrategies.OrderBy(s => DisplayRank(s.FormatId))];

    private static readonly string[] s_shippedOrder =
    [
        FormatIds.Json, FormatIds.Namespaced, FormatIds.Po, FormatIds.Ini, FormatIds.Yaml, FormatIds.Toml, FormatIds.AndroidXml,
        FormatIds.IosStrings, FormatIds.Xliff, FormatIds.Arb, FormatIds.Csv, FormatIds.Resx, FormatIds.JavaProperties, FormatIds.LaravelPhp,
    ];

    private static int DisplayRank(string formatId)
    {
        var index = Array.FindIndex(s_shippedOrder, id => FormatIds.Comparer.Equals(id, formatId));
        return index < 0 ? int.MaxValue : index;
    }
}
