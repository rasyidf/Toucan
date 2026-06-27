using Toucan.Core.Services;
using Toucan.Core.Services.Providers;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class PretranslationServiceTests
{
    private static PretranslationService CreateService() =>
        new(new[] { new MockTranslationProvider() });

    [Fact]
    public async Task PreTranslate_FillsMissingTranslations()
    {
        var service = CreateService();

        var items = new List<TranslationItem>
        {
            new() { Namespace = "greeting.hello", Language = "en", Value = "Hello" },
            new() { Namespace = "greeting.hello", Language = "fr", Value = "" }
        };

        var result = await service.PreTranslateAsync(items);

        Assert.Single(result.Items);
        Assert.True(result.Items[0].Succeeded);
        // The service should also apply translation to the item directly
        Assert.False(string.IsNullOrEmpty(items[1].Value));
    }

    [Fact]
    public async Task PreTranslate_DoesNotOverwriteExistingTranslations()
    {
        var service = CreateService();

        var items = new List<TranslationItem>
        {
            new() { Namespace = "greeting.goodbye", Language = "en", Value = "Goodbye" },
            new() { Namespace = "greeting.goodbye", Language = "fr", Value = "Au revoir" }
        };

        var result = await service.PreTranslateAsync(items);

        // No jobs should be created since fr already has a value
        Assert.Empty(result.Items);
        Assert.Equal("Au revoir", items[1].Value);
    }

    [Fact]
    public async Task PreTranslate_OverwritesExisting_WhenOptionSet()
    {
        var service = CreateService();

        var items = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "en", Value = "Hello" },
            new() { Namespace = "key.a", Language = "fr", Value = "Existing" }
        };

        var context = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "en", Value = "Hello" }
        };

        var request = new PretranslationRequest
        {
            Items = items,
            ContextItems = context,
            Options = new PretranslationOptions { Overwrite = true }
        };

        var result = await service.PreTranslateAsync(request);

        // fr should be overwritten with a new translation
        var frResult = result.Items.FirstOrDefault(r => r.Language == "fr");
        Assert.NotNull(frResult);
        Assert.True(frResult.Succeeded);
        Assert.NotEqual("Existing", items[1].Value);
    }

    [Fact]
    public async Task PreTranslate_PreviewOnly_DoesNotMutateItems()
    {
        var service = CreateService();

        var items = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "en", Value = "Hello" },
            new() { Namespace = "key.a", Language = "fr", Value = "" }
        };

        var options = new PretranslationOptions { PreviewOnly = true };
        var result = await service.PreTranslateAsync(items, options);

        // Result should still contain the translation
        Assert.Single(result.Items);
        Assert.True(result.Items[0].Succeeded);
        // But the item should NOT be mutated
        Assert.Equal("", items[1].Value);
    }

    [Fact]
    public async Task PreTranslate_ReturnsEmptyResult_WhenNoProvider()
    {
        var service = new PretranslationService(Array.Empty<Toucan.Core.Contracts.ITranslationProvider>());

        var items = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "en", Value = "Hello" },
            new() { Namespace = "key.a", Language = "fr", Value = "" }
        };

        var result = await service.PreTranslateAsync(items);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PreTranslate_ReturnsEmptyResult_WhenNullRequest()
    {
        var service = CreateService();

        var result = await service.PreTranslateAsync((PretranslationRequest)null!);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PreTranslate_UsesContextItems_AsSource()
    {
        var service = CreateService();

        var targets = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "fr", Value = "" }
        };
        var context = new List<TranslationItem>
        {
            new() { Namespace = "key.a", Language = "en", Value = "Source from context" }
        };

        var request = new PretranslationRequest
        {
            Items = targets,
            ContextItems = context
        };

        var result = await service.PreTranslateAsync(request);

        Assert.Single(result.Items);
        Assert.True(result.Items[0].Succeeded);
        Assert.Contains("Source from context", result.Items[0].TranslatedValue);
    }
}
