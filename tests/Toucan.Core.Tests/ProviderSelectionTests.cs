using Toucan.Core.Services;
using Toucan.Core.Services.Providers;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class ProviderSelectionTests
{
    [Fact]
    public async Task SelectsProvider_ByName()
    {
        var google = new GoogleTranslationProvider();
        var mock = new MockTranslationProvider();
        var service = new PretranslationService(new ITranslationProvider[] { mock, google });

        var items = new[] { new TranslationItem { Namespace = "a.b", Language = "id-ID", Value = "" } };
        var context = new[] { new TranslationItem { Namespace = "a.b", Language = "en", Value = "Hello" } };

        var request = new PretranslationRequest { Items = items, ContextItems = context, Provider = "Google" };
        var result = await service.PreTranslateAsync(request);

        Assert.All(result.Items, r => Assert.Equal("Google", r.Provider));
    }

    [Fact]
    public async Task SelectsProvider_ByName_CaseInsensitive()
    {
        var mock = new MockTranslationProvider();
        var service = new PretranslationService(new ITranslationProvider[] { mock });

        var items = new[] { new TranslationItem { Namespace = "a.b", Language = "fr", Value = "" } };
        var context = new[] { new TranslationItem { Namespace = "a.b", Language = "en", Value = "Hello" } };

        var request = new PretranslationRequest { Items = items, ContextItems = context, Provider = "mock" };
        var result = await service.PreTranslateAsync(request);

        Assert.Single(result.Items);
        Assert.Equal("Mock", result.Items[0].Provider);
    }

    [Fact]
    public async Task FallsBackToFirst_WhenProviderNameNotFound()
    {
        var mock = new MockTranslationProvider();
        var service = new PretranslationService(new ITranslationProvider[] { mock });

        var items = new[] { new TranslationItem { Namespace = "a.b", Language = "fr", Value = "" } };
        var context = new[] { new TranslationItem { Namespace = "a.b", Language = "en", Value = "Hello" } };

        var request = new PretranslationRequest { Items = items, ContextItems = context, Provider = "NonExistent" };
        var result = await service.PreTranslateAsync(request);

        Assert.Single(result.Items);
        Assert.Equal("Mock", result.Items[0].Provider);
    }
}
