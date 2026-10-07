using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class SearchAndReplaceServiceTests
{
    private static readonly SearchAndReplaceService Service = new();

    private static TranslationItem[] Items() =>
    [
        new() { Namespace = "auth.login", Language = "en", Value = "Log in to the app" },
        new() { Namespace = "auth.login", Language = "id", Value = "Masuk ke app" },
        new() { Namespace = "checkout.pay", Language = "en", Value = "Pay now. Application fee: $5" },
        new() { Namespace = "checkout.internal.note", Language = "en", Value = "App note" },
    ];

    [Fact]
    public void Literal_IsCaseInsensitiveByDefault_AndTreatsRegexCharactersAsText()
    {
        var hits = Service.Search(Items(), "app", new SearchOptions());
        Assert.Equal(["auth.login", "auth.login", "checkout.pay", "checkout.internal.note"], hits.Select(h => h.Key));

        Assert.Single(Service.Search(Items(), "$5", new SearchOptions()));
    }

    [Fact]
    public void MatchCase_And_WholeWord_Narrow_TheResults()
    {
        Assert.Equal(2, Service.Search(Items(), "app", new SearchOptions(MatchCase: true)).Count); // "app" in two values; "App"/"Application" excluded
        Assert.Equal(["auth.login", "auth.login", "checkout.internal.note"],
            Service.Search(Items(), "app", new SearchOptions(WholeWord: true)).Select(h => h.Key)); // not "Application"
    }

    [Fact]
    public void KeyMatches_AreMarkedAndNeverReplaced()
    {
        var items = Items();
        var options = new SearchOptions();
        var hits = Service.Search(items, "login", options);
        Assert.Contains(hits, h => h.InKey);

        var changed = Service.ApplyReplace(items, hits, "login", "sign in", options);
        Assert.Equal(0, changed);
        Assert.Equal("auth.login", items[0].Namespace);
    }

    [Fact]
    public void AKeyMatchIsReportedOnce_NotOncePerLanguage() =>
        Assert.Single(Service.Search(Items(), "auth", new SearchOptions()), h => h.InKey);

    [Fact]
    public void Filters_LimitLanguagesAndKeys()
    {
        var onlyId = Service.Search(Items(), "app", new SearchOptions(Languages: ["id"]));
        Assert.Equal(["id"], onlyId.Select(h => h.Language).Distinct());

        var checkoutNoInternal = Service.Search(Items(), "app", new SearchOptions(IncludeKeys: ["checkout"], ExcludeKeys: ["*.internal.*"]));
        Assert.Equal(["checkout.pay"], checkoutNoInternal.Select(h => h.Key).Distinct());
    }

    [Fact]
    public void Replace_IsLiteral_UnlessRegex()
    {
        var items = Items();
        var literal = new SearchOptions();
        var hits = Service.Search(items, "Pay now", literal);
        Assert.Equal(1, Service.ApplyReplace(items, hits, "Pay now", "$1 please", literal));
        Assert.Equal("$1 please. Application fee: $5", items[2].Value);

        var regex = new SearchOptions(UseRegex: true);
        var rx = Service.Search(items, @"(Log) in", regex);
        Service.ApplyReplace(items, rx, @"(Log) in", "$1 on", regex);
        Assert.Equal("Log on to the app", items[0].Value);
    }

    [Theory]
    [InlineData("Hello", "world", "World")]
    [InlineData("HELLO", "world", "WORLD")]
    [InlineData("hello", "World", "world")]
    [InlineData("hELLo", "world", "world")]
    public void PreserveCase_FollowsTheMatchedText(string matched, string replacement, string expected) =>
        Assert.Equal(expected, SearchAndReplaceService.MatchCaseOf(matched, replacement));

    [Fact]
    public void Preview_FillsPerMatchReplacement_WithoutTouchingItems()
    {
        var items = Items();
        var options = new SearchOptions(PreserveCase: true);
        var hits = Service.Search(items, "log in", options);
        Service.PreviewReplace(hits, "log in", "sign in", options);

        var valueHit = Assert.Single(hits, h => !h.InKey);
        Assert.Equal("Sign in", valueHit.ReplacementText);
        Assert.Equal("Sign in to the app", valueHit.ReplacedValue);
        Assert.Equal("Log in to the app", items[0].Value);
    }

    [Fact]
    public void InvalidRegex_Throws_ArgumentException() =>
        Assert.Throws<ArgumentException>(() => Service.Search(Items(), "(", new SearchOptions(UseRegex: true)));
}
