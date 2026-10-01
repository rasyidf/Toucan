using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Core.Services;

/// <summary>
/// The built-in save strategies, usable without a DI container. Used as the fallback for code that has no
/// <see cref="ITranslationStrategyFactory"/> (plain model classes, tests); app code should resolve strategies
/// from the container so plugin formats are included.
/// </summary>
public static class BuiltInFormats
{
    private static readonly Lazy<IReadOnlyList<ISaveStrategy>> s_saveStrategies = new(Create);

    public static IReadOnlyList<ISaveStrategy> SaveStrategies => s_saveStrategies.Value;

    public static ISaveStrategy? FindSaveStrategy(string formatId) =>
        SaveStrategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));

    /// <summary>Default relative file path for a built-in format; <c>{language}.json</c> for anything else.</summary>
    public static string DefaultFilePath(string formatId, string language) =>
        FindSaveStrategy(formatId)?.DefaultFilePath(language) ?? $"{language}.json";

    private static List<ISaveStrategy> Create()
    {
        var fs = new FileService(NullLogger<FileService>.Instance);
        return
        [
            new JsonSaveStrategy(fs), new NamespacedSaveStrategy(fs), new PoSaveStrategy(fs),
            new IniSaveStrategy(fs), new YamlSaveStrategy(fs), new TomlSaveStrategy(fs),
            new AndroidXmlSaveStrategy(fs), new IosStringsSaveStrategy(fs), new XliffSaveStrategy(fs),
            new ArbSaveStrategy(fs), new CsvSaveStrategy(fs), new ResxSaveStrategy(fs),
            new JavaPropertiesSaveStrategy(fs), new LaravelPhpSaveStrategy(),
        ];
    }
}
