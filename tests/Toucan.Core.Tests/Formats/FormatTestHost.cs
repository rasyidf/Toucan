using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Toucan.Modules;

namespace Toucan.Core.Tests.Formats;

/// <summary>The shipped strategy set, resolved from the same composition the app uses, so tests never list formats by hand.</summary>
internal static class FormatTestHost
{
    private static readonly ServiceProvider s_services = Build();

    public static IReadOnlyList<ISaveStrategy> SaveStrategies { get; } = [.. s_services.GetServices<ISaveStrategy>()];
    public static IEnumerable<ISaveStrategy> EditableStrategies => SaveStrategies.Where(s => s is not Toucan.Core.Contracts.IResourceCapabilities capabilities ||
        capabilities.Permissions(new() { FormatId = s.FormatId }).EditKeys);
    public static IReadOnlyList<ILoadStrategy> LoadStrategies { get; } = [.. s_services.GetServices<ILoadStrategy>()];
    public static ITranslationStrategyFactory Factory { get; } = s_services.GetRequiredService<ITranslationStrategyFactory>();

    public static IReadOnlyList<IProjectCompatibilityImporter> Importers { get; } = [.. s_services.GetServices<IProjectCompatibilityImporter>()];

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        return services.BuildServiceProvider();
    }
}
