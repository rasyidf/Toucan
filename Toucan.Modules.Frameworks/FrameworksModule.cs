using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Services.Frameworks;

namespace Toucan.Modules;

public static class FrameworksModule
{
    public const string Id = "toucan.frameworks";

    /// <summary>Adds the built-in framework profiles as the <c>toucan.frameworks</c> module.</summary>
    public static IServiceCollection AddToucanFrameworksModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in framework profiles", "i18n framework conventions for file discovery and paths", typeof(FrameworksModule).Assembly), s =>
        {
            foreach (var scanner in new[] { "generic", "i18next", "transloco", "vue", "rails", "formatjs" })
                s.AddSingleton<ISourceScanner>(new FrameworkSourceScanner(scanner));
            s.AddSingleton<IFrameworkProfile, GenericJsonProfile>();
            s.AddSingleton<IFrameworkProfile, I18nextProfile>();
            s.AddSingleton<IFrameworkProfile, AndroidProfile>();
            s.AddSingleton<IFrameworkProfile, FlutterArbProfile>();
            s.AddSingleton<IFrameworkProfile, DotNetResxProfile>();
            s.AddSingleton<IFrameworkProfile, IosProfile>();
            s.AddSingleton<IFrameworkProfile, GettextProfile>();
            s.AddSingleton<IFrameworkProfile, RailsYamlProfile>();
            s.AddSingleton<IFrameworkProfile>(new ScriptFrameworkProfile("formatjs", "React / FormatJS", "formatjs"));
            s.AddSingleton<IFrameworkProfile>(new ScriptFrameworkProfile("angular-transloco", "Transloco", "transloco"));
            s.AddSingleton<IFrameworkProfile>(new ScriptFrameworkProfile("vue-json", "Vue i18n", "vue"));
            s.AddSingleton<IFrameworkProfile>(new ScriptFrameworkProfile("react-json", "React Intl / FormatJS", "formatjs"));
        });
}
