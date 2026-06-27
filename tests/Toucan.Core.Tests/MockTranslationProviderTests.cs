using Toucan.Core.Services.Providers;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class MockTranslationProviderTests
{
    [Fact]
    public async Task MockProvider_ReturnsTranslatedResults()
    {
        var provider = new MockTranslationProvider();

        var jobs = new List<PretranslationJob>
        {
            new("home.title", "Welcome", "en", "fr"),
            new("home.desc", "Description", "en", "fr")
        };

        var results = (await provider.PretranslateAsync(jobs)).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.All(results, r => Assert.Contains("[mock/fr]", r.TranslatedValue));
    }

    [Fact]
    public async Task MockProvider_FailsGracefully_WhenSourceTextEmpty()
    {
        var provider = new MockTranslationProvider();

        var jobs = new List<PretranslationJob>
        {
            new("empty.key", "", "en", "fr")
        };

        var results = (await provider.PretranslateAsync(jobs)).ToList();

        Assert.Single(results);
        Assert.False(results[0].Succeeded);
        Assert.NotNull(results[0].ErrorMessage);
    }

    [Fact]
    public async Task MockProvider_ReportsProgress()
    {
        var provider = new MockTranslationProvider();
        var jobs = new List<PretranslationJob>
        {
            new("a.b", "Hello", "en", "fr"),
            new("a.c", "World", "en", "fr"),
            new("a.d", "Test", "en", "fr")
        };

        var progressReports = new List<PretranslationProgress>();
        var progress = new DirectProgress<PretranslationProgress>(p => progressReports.Add(p));

        await provider.PretranslateAsync(jobs, progress: progress);

        Assert.Equal(3, progressReports.Count);
        Assert.Equal(3, progressReports.Last().Total);
        Assert.Equal(3, progressReports.Last().Completed);
    }

    [Fact]
    public async Task MockProvider_RespectsCancellation()
    {
        var provider = new MockTranslationProvider();
        var jobs = new List<PretranslationJob>
        {
            new("a.b", "Hello", "en", "fr"),
            new("a.c", "World", "en", "fr")
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = (await provider.PretranslateAsync(jobs, cancellationToken: cts.Token)).ToList();

        // First item should fail with cancellation
        Assert.Contains(results, r => !r.Succeeded && r.ErrorMessage == "Cancelled");
    }

    [Fact]
    public void MockProvider_NameIsMock()
    {
        var provider = new MockTranslationProvider();
        Assert.Equal("Mock", provider.Name);
    }
}
