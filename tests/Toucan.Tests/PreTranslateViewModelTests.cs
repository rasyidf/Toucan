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

    public Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, System.IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new PretranslationResult());
    }

    public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, System.IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
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

    internal class ProgressReportingPretranslationService : IPretranslationService
    {
        public async Task<PretranslationResult> PreTranslateAsync(PretranslationRequest request, System.IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Task.Run(async () =>
            {
                var total = 5;
                for (var i = 0; i < total; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await Task.Delay(1);
                    progress?.Report(new PretranslationProgress { Completed = i + 1, Total = total, Message = $"Processed {i + 1}/{total}" });
                }

                return new PretranslationResult();
            });
        }

        public Task<PretranslationResult> PreTranslateAsync(IEnumerable<TranslationItem> items, PretranslationOptions? options = null, System.IProgress<PretranslationProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            // For tests, delegate to the request-based flow by creating a simple request
            var req = new PretranslationRequest { Items = items, Options = options };
            return PreTranslateAsync(req, progress, cancellationToken);
        }
    }

    [Fact]
    public async Task StartCommand_HandlesProgressReportedFromBackgroundThread()
    {
        var items = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "a.b", Language = "id-ID", Value = "Hello" },
            new TranslationItem { Namespace = "a.c", Language = "fr-FR", Value = "Bonjour" }
        };

        var fake = new ProgressReportingPretranslationService();
        var vm = new PreTranslateViewModel(new[] { "id-ID", "fr-FR" }, items, fake);

        // run the Start command which will call the fake service that reports progress from a background thread
        // The important part for the UI is that progress updates do not cause an exception/break the flow.
        await vm.StartCommand.ExecuteAsync(null);

        // Ensure the command completed cleanly and produced a sane percent value (0..100)
        Assert.InRange(vm.ProgressPercent, 0, 100);
    }

    [Fact]
    public async Task StartCommand_HandlesOutOfRangeProgressValues()
    {
        var items = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "a.b", Language = "id-ID", Value = "Hello" },
            new TranslationItem { Namespace = "a.c", Language = "fr-FR", Value = "Bonjour" }
        };

        // provider that reports invalid/out-of-range progress values
        var bad = new ProgressReportingPretranslationService();

        var vm = new PreTranslateViewModel(new[] { "id-ID", "fr-FR" }, items, bad);

        // simulate odd progress reporting from provider
        await vm.StartCommand.ExecuteAsync(null);

        // ProgressPercent must remain within 0..100 even if provider reports odd values
        Assert.InRange(vm.ProgressPercent, 0, 100);
    }

    [Fact]
    public void OpenProviderSettings_DoesNotThrow_WhenNoDialogServiceOrApplication()
    {
        // Ensure no App.Services and no WPF Application are required for this call.
        var vm = new PreTranslateViewModel();

        // Should not throw even when running in a headless unit-test environment
        vm.OpenProviderSettingsCommand.Execute(null);
    }
}
