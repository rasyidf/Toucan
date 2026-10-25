using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R1: first-run setup persists language and theme; the feature tour shows once.</summary>
public class OnboardingAndTourTests
{
    [AvaloniaFact]
    public void Onboarding_PersistsDefaultLanguageAndTheme()
    {
        using var host = new TestHost();
        var prefs = host.Services.GetRequiredService<IPreferenceService>();
        var vm = host.Services.GetRequiredService<OnboardingViewModel>();

        vm.DefaultLanguage = "fr-FR";
        vm.Theme = "Dark";
        vm.FinishCommand.Execute(null);

        var saved = prefs.Load();
        Assert.Equal("fr-FR", saved.DefaultLanguage);
        Assert.Equal("Dark", saved.Theme);
    }

    [AvaloniaFact]
    public void Onboarding_VersionBumpedSoExistingUsersSeeTheNewSteps()
    {
        Assert.True(OnboardingViewModel.CurrentVersion >= 2);
    }

    [AvaloniaFact]
    public void FeatureTour_AdvancesThroughStepsThenFinishes()
    {
        var finished = 0;
        var tour = new FeatureTourViewModel(() => finished++);
        tour.Show();

        Assert.True(tour.IsVisible);
        Assert.Equal(0, tour.Index);
        Assert.False(tour.IsLastStep);

        for (var i = 0; i < tour.Steps.Count - 1; i++) tour.NextCommand.Execute(null);
        Assert.True(tour.IsLastStep);

        tour.NextCommand.Execute(null); // Next on the last step finishes
        Assert.False(tour.IsVisible);
        Assert.Equal(1, finished);
    }

    [AvaloniaFact]
    public void FeatureTour_SkipFinishesImmediately()
    {
        var finished = 0;
        var tour = new FeatureTourViewModel(() => finished++);
        tour.Show();

        tour.SkipCommand.Execute(null);

        Assert.False(tour.IsVisible);
        Assert.Equal(1, finished);
    }

    [AvaloniaFact]
    public async Task FeatureTour_ShowsOnceThenIsRememberedAsSeen()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("tour", ("en", """{"a":"A"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        vm.ShowFeatureTourIfNeeded();
        Assert.NotNull(vm.FeatureTour);
        Assert.True(vm.FeatureTour!.IsVisible);

        vm.FeatureTour.SkipCommand.Execute(null); // dismiss → marks seen
        Assert.True(vm.AppOptions.FeatureTourSeen);

        // A later trigger does nothing because it is already seen.
        vm.ShowFeatureTourIfNeeded();
        Assert.False(vm.FeatureTour.IsVisible);
    }
}
