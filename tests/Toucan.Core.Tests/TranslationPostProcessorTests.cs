using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public class TranslationPostProcessorTests
{
    [Theory]
    [InlineData("Hello {{name}}", "{{name}}")]
    [InlineData("You have {count} items", "{count}")]
    [InlineData("Hi %s, you have %d msgs", "%s", "%d")]
    public void ProtectParameters_ExtractsPlaceholders(string source, params string[] expectedPlaceholders)
    {
        var (cleaned, placeholders) = TranslationPostProcessor.ProtectParameters(source);

        // Verify placeholders were extracted
        Assert.Equal(expectedPlaceholders.Length, placeholders.Count);
        for (int i = 0; i < expectedPlaceholders.Length; i++)
            Assert.Equal(expectedPlaceholders[i], placeholders[i]);

        // Verify the cleaned text contains indexed markers
        foreach (var placeholder in expectedPlaceholders)
            Assert.DoesNotContain(placeholder, cleaned);
    }

    [Fact]
    public void RestoreParameters_ReinsertesPlaceholders()
    {
        var placeholders = new List<string> { "{{name}}", "{count}" };
        var translatedWithMarkers = "Bonjour ⟨0⟩, vous avez ⟨1⟩ messages";

        var result = TranslationPostProcessor.RestoreParameters(translatedWithMarkers, placeholders);

        Assert.Equal("Bonjour {{name}}, vous avez {count} messages", result);
    }

    [Fact]
    public void RestoreParameters_NoOp_WhenNoPlaceholders()
    {
        var result = TranslationPostProcessor.RestoreParameters("Hello world", new List<string>());
        Assert.Equal("Hello world", result);
    }

    [Theory]
    [InlineData("Hello", "bonjour", "Bonjour")]
    [InlineData("hello", "bonjour", "bonjour")]
    [InlineData("Hello", "Bonjour", "Bonjour")]
    public void PreserveUppercaseFirst_MaintainsSourceCasing(string source, string translated, string expected)
    {
        var result = TranslationPostProcessor.PreserveUppercaseFirst(source, translated);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PreserveUppercaseFirst_HandlesEmptyStrings()
    {
        // When source is empty, translated is returned unchanged
        Assert.Equal("hello", TranslationPostProcessor.PreserveUppercaseFirst("", "hello"));
        // When translated is empty, it returns empty
        Assert.Equal("", TranslationPostProcessor.PreserveUppercaseFirst("Hello", ""));
    }

    [Fact]
    public void Process_AppliesFullPipeline()
    {
        var result = TranslationPostProcessor.Process("Welcome back", "bienvenue");
        // Source starts uppercase, so result should too
        Assert.Equal("Bienvenue", result);
    }

    [Fact]
    public void Process_ReturnsEmpty_WhenTranslationEmpty()
    {
        var result = TranslationPostProcessor.Process("Hello", "");
        Assert.Equal("", result);
    }
}
