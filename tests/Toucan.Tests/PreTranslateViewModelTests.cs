using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Toucan.ViewModels;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Tests;

internal class FakePretranslationServiceForVm : IPretranslationService
{
    public PretranslationRequest? LastRequest { get; private set; }

    public Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request)
    {
        LastRequest = request;
        return Task.FromResult(new PretranslationResult());
    }

    public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null)
    {
        LastRequest = new PretranslationRequest { Items = items, Options = options };
        return Task.FromResult(new PretranslationResult());
    }
}

public class PreTranslateViewModelTests
{
    [Fact]
    public async Task StartCommand_CallsPretranslationService_WithSelectedLanguages()
    {
        var items = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "a.b", Language = "id-ID", Value = "" },
            new TranslationItem { Namespace = "a.c", Language = "fr-FR", Value = "" }
        };

        var fake = new FakePretranslationServiceForVm();
        var vm = new PreTranslateViewModel(new[] { "id-ID", "fr-FR" }, items, fake);

        // deselect one language
        vm.AvailableLanguages.First(l => l.Name == "fr-FR").IsSelected = false;

        vm.SelectedProvider = "Google";
        await vm.StartCommand.ExecuteAsync(null);

        Assert.NotNull(fake.LastRequest);
        Assert.Equal("Google", fake.LastRequest.Provider);
        Assert.Single(fake.LastRequest.Items); // only id-ID should be included
    }
}
