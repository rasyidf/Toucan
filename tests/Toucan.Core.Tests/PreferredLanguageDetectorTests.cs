using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public class PreferredLanguageDetectorTests
{
    private static Project P(string name, string? language, int minutesAgo) =>
        new() { Path = "/" + name, PrimaryLanguage = language, LastOpened = new DateTime(2026, 10, 1, 12, 0, 0).AddMinutes(-minutesAgo) };

    [Fact]
    public void Detect_UsesTheMostRecentProjectThatHasALanguage()
    {
        var recent = new[] { P("old", "fr", 60), P("none", null, 1), P("new", "id-ID", 5) };

        Assert.Equal("id-ID", PreferredLanguageDetector.Detect(recent));
    }

    [Fact]
    public void Detect_ReturnsNull_WhenNothingRecordedALanguage()
    {
        Assert.Null(PreferredLanguageDetector.Detect([P("a", null, 1), P("b", " ", 2)]));
        Assert.Null(PreferredLanguageDetector.Detect([]));
    }

    [Theory]
    [InlineData(false, "en-US")]
    [InlineData(true, "id-ID")]
    public void Resolve_OnlyDetects_WhenToggledOn(bool detect, string expected) =>
        Assert.Equal(expected, PreferredLanguageDetector.Resolve("en-US", detect, [P("a", "id-ID", 1)]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_FallsBackToEnglish_WhenNoDefaultAndNothingDetected(string? configured) =>
        Assert.Equal("en-US", PreferredLanguageDetector.Resolve(configured, true, []));
}
