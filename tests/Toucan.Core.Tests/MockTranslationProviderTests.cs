using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Services.Providers;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class MockTranslationProviderTests
{
    [Fact]
    public async Task MockProvider_ShouldReturnTranslatedResults_WhenItemsMissing()
    {
        var provider = new MockTranslationProvider();

        var items = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "home.title", Language = "fr", Value = "" },
            new TranslationItem { Namespace = "home.desc", Language = "fr", Value = "" }
        };

        var results = (await provider.PretranslateAsync(items)).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded || r.ErrorMessage == "Skipped — existing translation present"));
    }
}
