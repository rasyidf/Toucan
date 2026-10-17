using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.Validation;
using Xunit;

namespace Toucan.Core.Tests;

public class PlaceholderValidationTests
{
    [Theory]
    [InlineData("{{name}}")]
    [InlineData("{count}")]
    [InlineData("{0}")]
    [InlineData("%s")]
    [InlineData("%1$s")]
    [InlineData(":name")]
    [InlineData("${name}")]
    public void TokensAreExtractedOnceWithTheCorrectSpan(string token)
    {
        var text = "Hello " + token + "!";
        var span = Assert.Single(PlaceholderService.Extract(text));
        Assert.Equal(token, span.Text);
        Assert.Equal(6, span.Position);
        Assert.Equal(token, text.Substring(span.Position, span.Text.Length));
    }

    [Theory]
    [InlineData("Hello {{name}}", "Bonjour {{other}}", "{{name}}", "{{other}}")]
    [InlineData("{0} {0}", "{0} {1}", "{0}", "{1}")]
    [InlineData("Hello %s", "Bonjour %d", "%s", "%d")]
    public void RuleReportsWrongNamesEvenWhenCountsMatch(string source, string target, string missing, string extra)
    {
        var result = Assert.Single(new PlaceholderMismatchRule().Validate(new ValidationContext
        {
            PrimaryLanguage = "en",
            Items = [new TranslationItem { Language = "en", Namespace = "a", Value = source },
                new TranslationItem { Language = "fr", Namespace = "a", Value = target }]
        }));
        Assert.Equal(ValidationSeverity.Error, result.Severity);
        Assert.Contains("Missing: " + missing, result.Message);
        Assert.Contains("Unexpected: " + extra, result.Message);
    }

    [Fact]
    public void ReorderingTokensDoesNotProduceAWarning()
    {
        Assert.True(PlaceholderService.Validate("{{name}} {0} %s", "%s {{name}} {0}").IsValid);
    }
}
