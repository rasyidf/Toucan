using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public sealed class PluginCompatibilityTests
{
    private static PluginManifest Manifest(string api = "1.0", string? min = null, string[]? platforms = null) => new()
    {
        Id = "acme.x", Name = "X", Version = "1.0.0", ApiVersion = api, EntryAssembly = "X.dll",
        MinHostVersion = min, Platforms = platforms ?? [],
    };

    [Fact]
    public void AMatchingPluginHasNoProblem() =>
        Assert.Null(PluginCompatibility.Check(Manifest(min: "0.23.0", platforms: ["macos", "linux"]), new Version(0, 23, 1), "macos"));

    [Fact]
    public void ANewerMinorApiSaysToUpdateToucan()
    {
        var message = PluginCompatibility.Check(Manifest(api: "1.9"), new Version(1, 0), "linux");
        Assert.Contains("newer", message);
        Assert.Contains("Update Toucan", message);
    }

    [Fact]
    public void ADifferentMajorApiSaysToAskTheAuthor()
    {
        var message = PluginCompatibility.Check(Manifest(api: "2.0"), new Version(1, 0), "linux");
        Assert.Contains("Ask the author", message);
    }

    [Fact]
    public void AnOlderHostIsNamedWithBothVersions()
    {
        var message = PluginCompatibility.Check(Manifest(min: "0.24.0"), new Version(0, 23, 0, 7), "linux");
        Assert.Contains("0.24.0", message);
        Assert.Contains("0.23.0", message);
    }

    [Fact]
    public void AnUnlistedPlatformSaysWhatIsSupported()
    {
        var message = PluginCompatibility.Check(Manifest(platforms: ["windows"]), new Version(1, 0), "macos");
        Assert.Contains("windows", message);
        Assert.Contains("macos", message);
    }

    [Theory]
    [InlineData("""{"id":"a.b","name":"A","version":"1.0.0","apiVersion":"1.0","entryAssembly":"A.dll","minHostVersion":"soon"}""", "minHostVersion")]
    [InlineData("""{"id":"a.b","name":"A","version":"1.0.0","apiVersion":"1.0","entryAssembly":"A.dll","platforms":["beos"]}""", "Unknown platform")]
    public void ManifestRejectsMalformedRequirements(string json, string expected)
    {
        Assert.False(PluginManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestReadsRequirements()
    {
        Assert.True(PluginManifest.TryParse("""{"id":"a.b","name":"A","version":"1.0.0","apiVersion":"1.0","entryAssembly":"A.dll","minHostVersion":"0.23.0","platforms":["linux"]}""", out var m, out _));
        Assert.Equal(new Version(0, 23, 0), m.ParsedMinHostVersion);
        Assert.Equal(["linux"], m.Platforms);
    }
}
