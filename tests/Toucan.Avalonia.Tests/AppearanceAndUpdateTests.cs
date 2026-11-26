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
        Assert.Equal("0.25.0-preview.1", UpdateService.Pick(doc.RootElement, true, new Version(0, 24, 0)).Version);
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

    [Theory]
    [InlineData("1.0.0", "1.0.0-rc.2", 1)]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2", 1)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.3", 1)]
    [InlineData("1.0.1-rc.1", "1.0.0", 1)]
    [InlineData("v1.0.0", "1.0.0", 0)]
    public void ReleaseVersionsOrderPreReleasesBeforeTheirRelease(string newer, string older, int sign)
    {
        Assert.True(ReleaseVersion.TryParse(newer, out var a));
        Assert.True(ReleaseVersion.TryParse(older, out var b));
        Assert.Equal(sign, Math.Sign(a.CompareTo(b)));
    }

    [Fact]
    public void AReleaseCandidateIsToldAboutItsFinalRelease()
    {
        using var doc = JsonDocument.Parse("""[ { "tag_name": "v1.0.0", "prerelease": false, "draft": false, "html_url": "https://example.com/v1" } ]""");
        var result = UpdateService.Pick(doc.RootElement, includePreview: false, "1.0.0-rc.2");
        Assert.True(result.IsNewer);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(UpdateService.Pick(doc.RootElement, includePreview: false, "1.0.0").IsNewer);
    }

    [AvaloniaFact]
    public async Task TheStartupCheckNotifiesOnceADayAndOnlyWhenEnabled()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var center = host.Services.GetRequiredService<Toucan.Core.Plugins.INotificationCenter>();
        var calls = 0;
        Task<UpdateCheckResult> Newer(string channel, string current)
        {
            calls++;
            return Task.FromResult(new UpdateCheckResult(false, true, "9.9.9", new Uri("https://example.com/v9.9.9"), false));
        }
        var time = new Clock(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));

        vm.AppOptions.CheckForUpdatesOnStartup = false;
        await vm.CheckForUpdatesOnStartupAsync(time, Newer);
        Assert.Equal(0, calls);

        vm.AppOptions.CheckForUpdatesOnStartup = true;
        vm.AppOptions.LastUpdateCheckUtc = null;
        await vm.CheckForUpdatesOnStartupAsync(time, Newer);
        var notice = Assert.Single(center.History, n => n.Content.Title.Contains("9.9.9", StringComparison.Ordinal));
        Assert.Equal(BuiltInCommands.IdFor("Help", "Download Update"), notice.Content.ActionCommandId);
        Assert.Equal(time.GetUtcNow().UtcDateTime, vm.AppOptions.LastUpdateCheckUtc);

        time.Advance(TimeSpan.FromHours(2));
        await vm.CheckForUpdatesOnStartupAsync(time, Newer);
        Assert.Equal(1, calls);

        time.Advance(TimeSpan.FromDays(1));
        await vm.CheckForUpdatesOnStartupAsync(time, (_, _) => { calls++; return Task.FromResult(new UpdateCheckResult(true, false, null, null, false)); });
        Assert.Equal(2, calls);
        Assert.Single(center.History, n => n.Content.Title.Contains("is available", StringComparison.Ordinal));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
