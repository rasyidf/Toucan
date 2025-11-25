using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Services;
using Toucan.Core.Services.Providers;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests;

public class ProviderSelectionTests
{
    [Fact]
    public async Task PretranslationService_SelectsProvider_ByName()
    {
        var google = new GoogleTranslationProvider();
        var mock = new MockTranslationProvider();
        var service = new PretranslationService(new Toucan.Core.Contracts.ITranslationProvider[] { mock, google });

        var items = new[] { new TranslationItem { Namespace = "a.b", Language = "id-ID", Value = "" } };

        var request = new PretranslationRequest { Items = items, Provider = "Google" };
        var res = await service.PreTranslateAsync(request);

        Assert.All(res.Items, r => Assert.Equal("Google", r.Provider));
    }
}
