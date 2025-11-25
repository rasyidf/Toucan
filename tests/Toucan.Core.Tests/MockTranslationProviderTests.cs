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

        var jobs = new List<PretranslationJob>
        {
            new PretranslationJob { Namespace = "home.title", SourceText = "Welcome", SourceLanguage = "en", TargetLanguage = "fr" },
            new PretranslationJob { Namespace = "home.desc", SourceText = "Description", SourceLanguage = "en", TargetLanguage = "fr" }
        };

        var results = (await provider.PretranslateAsync(jobs)).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.All(results, r => Assert.Contains("[mock/", r.TranslatedValue));
    }
}
