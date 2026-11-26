using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core;
using Toucan.Core.Contracts.Services;
using Toucan.Modules;

namespace Toucan.Avalonia;

/// <summary>
/// <c>Toucan --self-check</c>: proves a packaged build starts and its format modules load, without opening a window. The release
/// workflow runs it on every package; it prints the version and exits 0, or prints the failure and exits 1.
/// </summary>
internal static class SelfCheck
{
    public static int Run(TextWriter output)
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
        var folder = Directory.CreateTempSubdirectory("toucan-self-check-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "en.json"), """{"greeting":"Hello"}""");
            File.WriteAllText(Path.Combine(folder, "fr.json"), """{"greeting":"Bonjour"}""");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToucanCore();
            services.AddToucanDefaults();
            using var provider = services.BuildServiceProvider();
            var loaded = provider.GetRequiredService<IProjectService>().LoadProject(folder);
            var count = loaded.Translations.Count;
            if (count != 2) throw new InvalidOperationException($"Expected 2 translations from the sample project, read {count}.");
            var formats = provider.GetServices<ISaveStrategy>().Count();
            output.WriteLine($"Toucan {version} self-check passed ({RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {formats} formats).");
            return 0;
        }
        catch (Exception ex)
        {
            output.WriteLine($"Toucan {version} self-check failed: {ex}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
        }
    }
}
