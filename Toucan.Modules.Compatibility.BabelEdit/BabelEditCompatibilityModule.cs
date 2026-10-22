using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using System.Text.Json;

namespace Toucan.Modules;

public static class BabelEditCompatibilityModule
{
    public const string Id = "toucan.compatibility.babeledit";
    public static IServiceCollection AddToucanBabelEditCompatibilityModule(this IServiceCollection services) =>
        services.AddToucanModule(new BuiltInModule(Id, "BabelEdit project compatibility", "Migration of user-owned project files", typeof(BabelEditCompatibilityModule).Assembly),
            s => s.AddSingleton<IProjectCompatibilityImporter, BabelEditImporter>());
}

public sealed class BabelEditImporter : IProjectCompatibilityImporter
{
    public IEnumerable<string> FindProjects(string folder) => Directory.EnumerateFiles(folder, "*.babel");
    public CompatibilityProject Read(string file) => BabelProjectReader.Read(file);
    public void ApplyMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items) => BabelProjectReader.ApplyMetadata(compatibility, items);
    public void CaptureMetadata(IDictionary<string, CompatibilityEnvelope> compatibility, IEnumerable<TranslationItem> items) => BabelProjectReader.CaptureMetadata(compatibility, items);
}
