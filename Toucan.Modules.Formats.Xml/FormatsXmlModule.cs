using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;

namespace Toucan.Modules;

public static class FormatsXmlModule
{
    public const string Id = "toucan.formats.xml";

    /// <summary>Adds the Xml family of built-in file formats as the <c>toucan.formats.xml</c> module.</summary>
    public static IServiceCollection AddToucanFormatsXmlModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in formats: XML family", "Android strings.xml, XLIFF and .resx", typeof(FormatsXmlModule).Assembly), s =>
        {
            s.AddFormatStrategy<ISaveStrategy, AndroidXmlSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, XliffSaveStrategy>();
            s.AddFormatStrategy<ISaveStrategy, ResxSaveStrategy>();
            s.AddFormatStrategy<ILoadStrategy, AndroidXmlLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, XliffLoadStrategy>();
            s.AddFormatStrategy<ILoadStrategy, ResxLoadStrategy>();
        });
}
