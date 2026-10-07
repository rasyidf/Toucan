using Microsoft.Extensions.DependencyInjection;

namespace Toucan.Modules;

public static class ToucanDefaults
{
    /// <summary>
    /// Adds every built-in module. Call it after <c>AddToucanCore</c> and before <c>AddToucanPlugins</c>, so the
    /// modules' IDs are known to the plugin host and plugins cannot take them.
    /// </summary>
    public static IServiceCollection AddToucanDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddToucanFormatsJsonModule()
            .AddToucanFormatsXmlModule()
            .AddToucanFormatsTextModule()
            .AddToucanFormatsDataModule()
            .AddToucanFrameworksModule()
            .AddToucanProvidersModule()
            .AddToucanValidationModule();
    }
}
