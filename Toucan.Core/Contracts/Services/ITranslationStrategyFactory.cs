using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface ITranslationStrategyFactory
{
    ISaveStrategy? GetSaveStrategy(string formatId);
    ILoadStrategy? GetLoadStrategy(string formatId);
    ILoadStrategy? GetManifestLoadStrategy();

    /// <summary>All registered save strategies (built-in and plugin), in registration order.</summary>
    IReadOnlyList<ISaveStrategy> SaveStrategies { get; }
}

/// <summary>Compatibility overloads for callers that still hold a <see cref="SaveStyles"/>.</summary>
public static class TranslationStrategyFactoryExtensions
{
    public static ISaveStrategy? GetSaveStrategy(this ITranslationStrategyFactory factory, SaveStyles style) =>
        factory.GetSaveStrategy(FormatIds.FromStyle(style));

    public static ILoadStrategy? GetLoadStrategy(this ITranslationStrategyFactory factory, SaveStyles style) =>
        factory.GetLoadStrategy(FormatIds.FromStyle(style));
}
