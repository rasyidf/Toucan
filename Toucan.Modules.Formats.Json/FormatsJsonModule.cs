using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Modules;

public static class FormatsJsonModule
{
    public const string Id = "toucan.formats.json";

    /// <summary>Adds the Json family of built-in file formats as the <c>toucan.formats.json</c> module.</summary>
    public static IServiceCollection AddToucanFormatsJsonModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in formats: JSON family", "JSON, namespaced JSON (i18next), project manifest and Flutter ARB", typeof(FormatsJsonModule).Assembly), s =>
        {
            s.AddFormatStrategy<ISaveStrategy, JsonSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, NamespacedSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, ArbSaveStrategy>();
            s.AddFormatStrategy<ILoadStrategy, JsonLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, NamespacedLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, ManifestLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, ArbLoadStrategy>();
        });
}
