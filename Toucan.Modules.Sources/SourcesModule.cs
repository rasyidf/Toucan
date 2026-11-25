using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Plugins;
using Toucan.Core.Contracts;
using Toucan.Modules.Sources;

namespace Toucan.Modules;

public static class SourcesModule
{
    public const string Id = "toucan.sources";

    /// <summary>Adds the built-in content-source adapters (the local folder) as the <c>toucan.sources</c> module.</summary>
    public static IServiceCollection AddToucanSourcesModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "Built-in content sources", "The local-folder content source and the Toucan source protocol", typeof(SourcesModule).Assembly), s =>
        {
            s.AddBuiltInSourceAdapter(new LocalFolderSourceAdapter());
            s.AddBuiltInSourceAdapter(new RestSourceAdapter());
        });
}
