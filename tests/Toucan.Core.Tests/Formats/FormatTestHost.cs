using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Core.Tests.Formats;

/// <summary>Builds the same strategy set that App.ConfigureServices registers, without DI.</summary>
internal static class FormatTestHost
{
    public static IReadOnlyList<ISaveStrategy> SaveStrategies { get; } = CreateSaveStrategies();
    public static IReadOnlyList<ILoadStrategy> LoadStrategies { get; } = CreateLoadStrategies();
    public static TranslationStrategyFactory Factory { get; } = new(SaveStrategies, LoadStrategies);

    private static List<ISaveStrategy> CreateSaveStrategies()
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

    private static List<ILoadStrategy> CreateLoadStrategies()
    {
        var fs = new FileService(NullLogger<FileService>.Instance);
        var json = new JsonLoadStrategy(fs, NullLogger<JsonLoadStrategy>.Instance);
        return
        [
            json, new NamespacedLoadStrategy(json),
            new ManifestLoadStrategy(fs, NullLogger<ManifestLoadStrategy>.Instance),
            new YamlLoadStrategy(), new TomlLoadStrategy(), new AndroidXmlLoadStrategy(),
            new IosStringsLoadStrategy(), new XliffLoadStrategy(), new ArbLoadStrategy(),
            new CsvLoadStrategy(), new ResxLoadStrategy(), new PoLoadStrategy(),
            new JavaPropertiesLoadStrategy(), new LaravelPhpLoadStrategy(),
        ];
    }
}
