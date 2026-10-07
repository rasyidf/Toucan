using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Modules;

public static class FormatsDataModule
{
    public const string Id = "toucan.formats.data";

    /// <summary>Adds the Data family of built-in file formats as the <c>toucan.formats.data</c> module.</summary>
    public static IServiceCollection AddToucanFormatsDataModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in formats: data family", "YAML and TOML", typeof(FormatsDataModule).Assembly), s =>
        {
            s.AddFormatStrategy<ISaveStrategy, YamlSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, TomlSaveStrategy>();
            s.AddFormatStrategy<ILoadStrategy, YamlLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, TomlLoadStrategy>();
        });
}
