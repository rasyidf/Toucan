using System.Reflection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public class PluginContractTests
{
    private const string Valid = """
        {
          "id": "acme.po-plus",
          "name": "Acme PO Plus",
          "version": "1.2.3",
          "apiVersion": "1.0",
          "entryAssembly": "Acme.PoPlus.dll",
          "author": "Acme",
          "capabilities": ["formats", "validation"]
        }
        """;

    [Fact]
    public void ValidManifestParses()
    {
        Assert.True(PluginManifest.TryParse(Valid, out var m, out var errors), string.Join("; ", errors));
        Assert.Equal("acme.po-plus", m.Id);
        Assert.Equal("Acme.PoPlus.dll", m.EntryAssembly);
        Assert.Equal(new Version(1, 0), m.ParsedApiVersion);
        Assert.Equal(["formats", "validation"], m.Capabilities);
    }

    [Fact]
    public void ManifestToleratesCommentsTrailingCommasAndCasing()
    {
        const string json = """
            { // a comment
              "ID": "acme.x", "Name": "X", "Version": "1.0.0", "ApiVersion": "1.0", "EntryAssembly": "X.dll",
            }
            """;
        Assert.True(PluginManifest.TryParse(json, out var m, out var errors), string.Join("; ", errors));
        Assert.Equal("acme.x", m.Id);
        Assert.Empty(m.Capabilities);
    }

    [Theory]
    [InlineData("", "not valid JSON")]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("null", "empty")]
    public void UnreadableManifestFailsWithoutThrowing(string json, string expected)
    {
        Assert.False(PluginManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("\"id\": \"acme.po-plus\"", "\"id\": \"Acme Plus\"", "'id' must use")]
    [InlineData("\"id\": \"acme.po-plus\"", "\"id\": \"\"", "'id' is required")]
    [InlineData("\"version\": \"1.2.3\"", "\"version\": \"banana\"", "'version'")]
    [InlineData("\"apiVersion\": \"1.0\"", "\"apiVersion\": \"x\"", "'apiVersion'")]
    [InlineData("\"entryAssembly\": \"Acme.PoPlus.dll\"", "\"entryAssembly\": \"../Evil.dll\"", "inside the plugin folder")]
    [InlineData("\"entryAssembly\": \"Acme.PoPlus.dll\"", "\"entryAssembly\": \"sub/Evil.dll\"", "inside the plugin folder")]
    [InlineData("\"entryAssembly\": \"Acme.PoPlus.dll\"", "\"entryAssembly\": \"/abs/Evil.dll\"", "inside the plugin folder")]
    [InlineData("\"entryAssembly\": \"Acme.PoPlus.dll\"", "\"entryAssembly\": \"Evil.exe\"", "must end with .dll")]
    [InlineData("\"formats\"", "\"telepathy\"", "Unknown capability")]
    public void InvalidFieldsAreReported(string original, string replacement, string expected)
    {
        var json = Valid.Replace(original, replacement, StringComparison.Ordinal);
        Assert.NotEqual(Valid, json);

        Assert.False(PluginManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        Assert.False(PluginManifest.TryParse("""{ "capabilities": ["nope"] }""", out _, out var errors));
        Assert.True(errors.Count >= 5, string.Join("; ", errors));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.1", false)] // newer minor than the host
    [InlineData("2.0", false)]
    [InlineData("0.9", false)]
    public void ApiCompatibilityIsSameMajorAndNotNewerMinor(string version, bool expected) =>
        Assert.Equal(expected, PluginApi.IsCompatible(Version.Parse(version)));

    [Fact]
    public void AbstractionsDoNotDependOnCoreOrUi()
    {
        // Plugin authors must be able to build against the abstractions alone.
        var assembly = typeof(IToucanPlugin).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("Toucan.Core", references);
        Assert.DoesNotContain(references, r => r!.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain("CommunityToolkit.Mvvm", references);
        Assert.Equal("Toucan.Plugins.Abstractions", assembly.GetName().Name);
    }

    [Fact]
    public void PluginFacingContractsLiveInTheAbstractionsAssembly()
    {
        var abstractions = typeof(IToucanPlugin).Assembly;
        foreach (var type in new[]
        {
            typeof(ISaveStrategy), typeof(ILoadStrategy), typeof(ITranslationProvider), typeof(IValidationRule),
            typeof(IFrameworkProfile), typeof(Toucan.Core.Models.TranslationItem), typeof(Toucan.Core.Models.FormatIds),
            typeof(Toucan.Core.Models.ProviderDefinition), typeof(IPluginContext),
        })
            Assert.Same(abstractions, type.Assembly);

        // The pipeline and project services are host internals and must not leak into the plugin contract.
        Assert.NotSame(abstractions, typeof(IValidationPipeline).Assembly);
        Assert.NotSame(abstractions, typeof(IProjectService).Assembly);
    }
}
