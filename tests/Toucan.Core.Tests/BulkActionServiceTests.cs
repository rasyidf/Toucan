using Toucan.Core.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class BulkActionServiceTests
{
    [Fact]
    public void GenerateStatistics_ReturnsLanguageBreakdown()
    {
        var service = new BulkActionService();

        var items = new List<TranslationItem>
        {
            new() { Namespace = "a.b", Language = "en", Value = "Hello" },
            new() { Namespace = "a.c", Language = "en", Value = "World" },
            new() { Namespace = "a.b", Language = "fr", Value = "Bonjour" },
            new() { Namespace = "a.c", Language = "fr", Value = "" }
        };

        var stats = service.GenerateStatistics(items);

        Assert.Contains("en", stats);
        Assert.Contains("fr", stats);
        Assert.Contains("100%", stats); // en is fully translated
    }

    [Fact]
    public async Task PreTranslate_FallbackCopiesSource_WhenNoPretranslationService()
    {
        var service = new BulkActionService(pretranslationService: null);

        var items = new List<TranslationItem>
        {
            new() { Namespace = "a.b", Language = "en", Value = "Hello" },
            new() { Namespace = "a.b", Language = "fr", Value = "" }
        };

        await service.PreTranslateAsync(items);

        // Fallback should copy first non-empty value
        Assert.Equal("Hello", items[1].Value);
    }
}
