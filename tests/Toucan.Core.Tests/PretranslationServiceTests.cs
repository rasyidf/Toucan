using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Toucan.Core.Services;
using Toucan.Core.Services.Providers;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class PretranslationServiceTests
{
    [Fact]
    public async Task PretranslationService_AppliesMockTranslations_ToItems()
    {
        // Arrange
        var provider = new MockTranslationProvider();
        var service = new PretranslationService(new[] { provider });

        var items = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "greeting.hello", Language = "fr", Value = "" },
            new TranslationItem { Namespace = "greeting.goodbye", Language = "fr", Value = "existing" }
        };

        // Act
        var result = await service.PreTranslateAsync(items);

        // Assert
        Assert.Equal(2, result.Items.Count);
        var applied = items.First(i => i.Namespace == "greeting.hello");
        Assert.False(string.IsNullOrEmpty(applied.Value));
    }
}
