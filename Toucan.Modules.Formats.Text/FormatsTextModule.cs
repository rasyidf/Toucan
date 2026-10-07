using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Modules;

public static class FormatsTextModule
{
    public const string Id = "toucan.formats.text";

    /// <summary>Adds the Text family of built-in file formats as the <c>toucan.formats.text</c> module.</summary>
    public static IServiceCollection AddToucanFormatsTextModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in formats: text family", "PO, INI, Java properties, iOS .strings, Laravel PHP and CSV", typeof(FormatsTextModule).Assembly), s =>
        {
            s.AddFormatStrategy<ISaveStrategy, PoSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, IniSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, JavaPropertiesSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, IosStringsSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, LaravelPhpSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, CsvSaveStrategy>();
            s.AddFormatStrategy<ILoadStrategy, PoLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, JavaPropertiesLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, IosStringsLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, LaravelPhpLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, CsvLoadStrategy>();
        });
}
