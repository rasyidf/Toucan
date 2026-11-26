using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>V1-51: a project above the measured supported scale opens with a notice; one within it opens without.</summary>
public sealed class SupportedScaleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-scale-notice-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private IReadOnlyList<string> Open(int keys)
    {
        foreach (var language in new[] { "en", "fr" })
            File.WriteAllText(Path.Combine(_root, language + ".json"),
                "{" + string.Join(",", Enumerable.Range(0, keys).Select(k => $"\"k{k}\":\"v{k}\"")) + "}");
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IProjectService>().LoadProject(_root).Warnings;
    }

    [Fact]
    public void AProjectWithinTheSupportedScaleOpensWithoutANotice() =>
        Assert.DoesNotContain(Open(ProjectService.SupportedTranslations / 2), w => w.Contains("is tested up to", StringComparison.Ordinal));

    [Fact]
    public void ALargerProjectOpensWithANotice() =>
        Assert.Contains(Open(ProjectService.SupportedTranslations / 2 + 1), w => w.Contains("is tested up to", StringComparison.Ordinal));
}
