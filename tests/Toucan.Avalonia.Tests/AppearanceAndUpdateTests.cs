using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class AppearanceAndUpdateTests
{
    private const string Releases = """
        [
          { "tag_name": "v0.25.0-preview.1", "prerelease": true,  "draft": false, "html_url": "https://github.com/rasyidf/Toucan/releases/tag/v0.25.0-preview.1" },
          { "tag_name": "v0.24.0",           "prerelease": false, "draft": false, "html_url": "https://github.com/rasyidf/Toucan/releases/tag/v0.24.0" },
          { "tag_name": "v0.26.0",           "prerelease": false, "draft": true,  "html_url": "https://github.com/rasyidf/Toucan/releases/tag/v0.26.0" },
          { "tag_name": "v0.23.0",           "prerelease": false, "draft": false, "html_url": "https://github.com/rasyidf/Toucan/releases/tag/v0.23.0" }
        ]
        """;

    [Theory]
    [InlineData("v0.24.0", true, "0.24.0")]
    [InlineData("0.24", true, "0.24")]
    [InlineData("v1.2.3-preview.4", true, "1.2.3")]
    [InlineData("nightly", false, "0.0")]
    [InlineData(null, false, "0.0")]
    public void TryParseTag_HandlesCommonTagShapes(string? tag, bool ok, string expected)
    {
        Assert.Equal(ok, UpdateService.TryParseTag(tag, out var v));
        Assert.Equal(expected, v.ToString());
    }

    [Fact]
    public void Pick_StableIgnoresPreviewsAndDrafts()
    {
        using var doc = JsonDocument.Parse(Releases);
        var result = UpdateService.Pick(doc.RootElement, includePreview: false, new Version(0, 23, 0));
        Assert.True(result.IsNewer);
        Assert.Equal("0.24.0", result.Version);
        Assert.False(result.IsPreview);
    }

    [Fact]
    public void Pick_PreviewChannelSeesPreReleases_AndUpToDateIsNotNewer()
    {
        using var doc = JsonDocument.Parse(Releases);
        Assert.Equal("0.25.0", UpdateService.Pick(doc.RootElement, true, new Version(0, 24, 0)).Version);
        Assert.False(UpdateService.Pick(doc.RootElement, false, new Version(0, 24, 0)).IsNewer);
    }

    [AvaloniaFact]
    public void ColorScheme_AppliesOverridesAndResetsToDefaults()
    {
        var defaults = ColorSchemeService.DefaultColor(ThemeVariant.Light, "CardBackgroundBrush");

        ColorSchemeService.Apply("Ocean", null, new Dictionary<string, string> { ["Light:CardBackgroundBrush"] = "#112233" });
        Assert.True(Application.Current!.TryGetResource("SettingsCardBackgroundBrush", ThemeVariant.Light, out var edited));
        Assert.Equal(Color.Parse("#112233"), ((ISolidColorBrush)edited!).Color);

        ColorSchemeService.Apply("Toucan", null, new Dictionary<string, string>());
        Assert.True(Application.Current.TryGetResource("CardBackgroundBrush", ThemeVariant.Light, out var reset));
        Assert.Equal(defaults, ((ISolidColorBrush)reset!).Color);
    }

    [AvaloniaFact]
    public void OptionsViewModel_SchemeEdits_PreviewLiveAndCancelRestores()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OptionsViewModel>();
        vm.EditingVariant = "Light";
        vm.AccentHex = "#123456";
        Assert.Equal(ColorSchemeService.Custom, vm.ColorScheme);

        var item = vm.SchemeColorItems.First(i => i.StorageKey == "Light:ChromeBackgroundBrush");
        item.Hex = "#ABCDEF";
        Assert.True(item.IsOverridden);
        Assert.True(Application.Current!.TryGetResource("ChromeBackgroundBrush", ThemeVariant.Light, out var live));
        Assert.Equal(Color.Parse("#ABCDEF"), ((ISolidColorBrush)live!).Color);

        item.Hex = "not a color";
        Assert.False(item.IsValid);

        vm.RevertSchemePreview();
        Assert.True(Application.Current.TryGetResource("ChromeBackgroundBrush", ThemeVariant.Light, out var back));
        Assert.Equal(ColorSchemeService.DefaultColor(ThemeVariant.Light, "ChromeBackgroundBrush"), ((ISolidColorBrush)back!).Color);
    }
}
