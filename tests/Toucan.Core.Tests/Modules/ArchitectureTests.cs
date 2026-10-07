using Xunit;
using Toucan.Core.Services;

namespace Toucan.Core.Tests.Modules;

/// <summary>Dependency rules of the module layout (docs/specs/plugin-modularization/design.md).</summary>
public class ArchitectureTests
{
    private const string CommonAssemblyName = "Toucan.Core.Common";
    private static readonly string[] s_modulePrefixes = ["Toucan.Modules."];

    [Fact]
    public void CoreAssemblyDoesNotReferenceAnyModule()
    {
        var core = typeof(ToucanCoreServiceCollectionExtensions).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(core, n => s_modulePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void CoreProjectFileDoesNotReferenceAnyModule()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "Toucan.Core", "Toucan.Core.csproj"));
        Assert.DoesNotContain("Toucan.Modules.", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void CommonAssemblyReferencesOnlyAbstractions()
    {
        var common = typeof(FileEnumerator).Assembly;
        Assert.Equal(CommonAssemblyName, common.GetName().Name);
        var toucan = common.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Toucan.", StringComparison.Ordinal));

        Assert.DoesNotContain("Toucan.Core", toucan);
        Assert.DoesNotContain(toucan, n => s_modulePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)));
    }

    [Fact]
    public void ModulesReferenceNeitherCoreNorEachOther()
    {
        // Modules are leaves: Common and Abstractions below, nothing else. Defaults is the only assembly that sees them all.
        foreach (var module in new[] { typeof(Toucan.Modules.ValidationModule).Assembly, typeof(Toucan.Modules.FrameworksModule).Assembly, typeof(Toucan.Modules.ProvidersModule).Assembly,
            typeof(Toucan.Modules.FormatsJsonModule).Assembly, typeof(Toucan.Modules.FormatsXmlModule).Assembly,
            typeof(Toucan.Modules.FormatsTextModule).Assembly, typeof(Toucan.Modules.FormatsDataModule).Assembly })
        {
            var toucan = module.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Toucan.", StringComparison.Ordinal)).ToList();

            Assert.DoesNotContain("Toucan.Core", toucan);
            Assert.DoesNotContain(toucan, n => n.StartsWith("Toucan.Modules.", StringComparison.Ordinal) && n != module.GetName().Name);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Toucan.CrossPlatform.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
