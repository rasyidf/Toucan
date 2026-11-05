using Avalonia.Headless.XUnit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Panels;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class TerminologyWorkflowTests
{
    [AvaloniaFact]
    public async Task GlossaryChangesFollowUndoAndSuggestionsUseNormalEditHistory()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("glossary", ("en", "{\"a\":\"Account\"}"), ("fr", "{\"a\":\"Mon \"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        vm.GlossaryTerm = "account"; vm.GlossaryLanguage = "fr"; vm.GlossaryTranslation = "compte";
        vm.SaveGlossaryTermCommand.Execute(null);
        Assert.Single(vm.ProjectSettings.Glossary); Assert.True(vm.IsDirty);
        vm.UndoCommand.Execute(null); Assert.Empty(vm.ProjectSettings.Glossary);
        vm.RedoCommand.Execute(null); Assert.Single(vm.ProjectSettings.Glossary);
        vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "a");
        Assert.Single(vm.GlossaryMatches);
        host.Dialogs.Picks.Enqueue("fr");
        await vm.InsertGlossaryTranslationCommand.ExecuteAsync(vm.GlossaryMatches[0]);
        Assert.Equal("Mon compte", vm.AllTranslation.Single(i => i.Language == "fr").Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Mon ", vm.AllTranslation.Single(i => i.Language == "fr").Value);
        Assert.Single(vm.ProjectSettings.Glossary);
    }

    [AvaloniaFact]
    public async Task ManualValidationHonorsProjectSeverityAndDisableSettings()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("policy", ("en", "{\"a\":\"Hi {name}\"}"), ("fr", "{\"a\":\"Salut\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        vm.ProjectSettings.ValidationRules = new() { ["placeholder-mismatch"] = new(true, "Info") };
        vm.RunValidationCommand.Execute(null);
        Assert.Contains(vm.ValidationIssues, r => r.RuleId == "placeholder-mismatch" && r.Severity == ValidationSeverity.Info);
        vm.ProjectSettings.ValidationRules["placeholder-mismatch"] = new(false, "Error");
        vm.RunValidationCommand.Execute(null);
        Assert.DoesNotContain(vm.ValidationIssues, r => r.RuleId == "placeholder-mismatch");
    }

    [AvaloniaFact]
    public async Task AnalysisStrictnessIsProjectConfigurationAndCanBeUndone()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("strictness", ("en", "{\"a\":\"Hi\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        host.Dialogs.Picks.Enqueue("Strict (confidence 0.4+)");
        await vm.ConfigureAnalysisStrictnessCommand.ExecuteAsync(null);
        Assert.Equal(0.4, vm.ProjectSettings!.AnalysisConfidenceThreshold);
        vm.UndoCommand.Execute(null); Assert.Equal(0.6, vm.ProjectSettings.AnalysisConfidenceThreshold);
    }

    [AvaloniaFact]
    public async Task SavingGlossaryConfigurationClearsDirtyStateAndReopensTheTerms()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("glossary-save", ("en", "{\"a\":\"Account\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.GlossaryTerm = "account"; vm.GlossaryLanguage = "fr"; vm.GlossaryTranslation = "compte";
        vm.SaveGlossaryTermCommand.Execute(null);
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsDirty);
        Assert.Equal("compte", Toucan.Core.Models.ProjectSettings.LoadFrom(folder)!.Glossary.Single().Translations["fr"]);
    }

    [AvaloniaFact]
    public async Task AiFindingRetranslationRequiresPreviewAcceptanceAndUndoesAsOneEdit()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("finding", ("en", "{\"a\":\"Account\"}"), ("fr", "{\"a\":\"Wrong\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        var issue = new ValidationIssueItem("a", "Wrong term", Namespace: "a", Language: "fr", RuleId: "ai-analysis");
        await vm.RetranslateFindingCommand.ExecuteAsync(issue);
        Assert.Equal("Wrong", vm.AllTranslation.Single(i => i.Language == "fr").Value);
        host.Dialogs.OnPreTranslate = preview =>
        {
            Assert.True(preview.OverwriteExisting);
            preview.PreviewResults.Add(new Toucan.Core.Models.PretranslationItemResult { Namespace = "a", Language = "fr", Succeeded = true, TranslatedValue = "Compte" });
            preview.CommitCommand.Execute(null);
        };
        await vm.RetranslateFindingCommand.ExecuteAsync(issue);
        Assert.Equal("Compte", vm.AllTranslation.Single(i => i.Language == "fr").Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Wrong", vm.AllTranslation.Single(i => i.Language == "fr").Value);
    }

    [AvaloniaFact]
    public async Task UnsavedGlossaryReachesProviderContextAndStaleAiFixIsRefused()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("provider-terms", ("en", "{\"a\":\"Account\"}"), ("fr", "{\"a\":\"Compte\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        vm.ProjectSettings.Glossary = [new() { Term = "account", Translations = new() { ["fr"] = "compte" } }];
        var options = ProviderOptionsBuilder.Build("AI", null, folder, vm.ProjectSettings, vm.AllTranslation);
        Assert.Contains("compte", options["glossary"], StringComparison.Ordinal);
        Assert.Contains("compte", options["context"], StringComparison.Ordinal);
        vm.ApplyFixCommand.Execute(new ValidationIssueItem("a", "Old finding", Namespace: "a", Language: "fr", SuggestedFix: "Stale", RuleId: "ai-analysis", ExpectedValue: "Old value", ExpectedSource: "Account"));
        Assert.Equal("Compte", vm.AllTranslation.Single(i => i.Language == "fr").Value);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DictionaryPanelLoadsItsBindings(bool dark)
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("dictionary-panel", ("en", "{\"a\":\"Account\"}"), ("fr", "{\"a\":\"Compte\"}")));
        vm.ProjectSettings!.PrimaryLanguage = "en";
        vm.GlossaryTerm = "account"; vm.GlossaryDescription = "Banking account"; vm.GlossaryLanguage = "fr"; vm.GlossaryTranslation = "compte";
        vm.SaveGlossaryTermCommand.Execute(null);
        vm.SelectedGroup = vm.PagingController.Data.Single();
        var panel = new DictionaryPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 340, Height = 760 };
        var previous = Application.Current!.RequestedThemeVariant;
        try
        {
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(panel.Content); Assert.Single(vm.GlossaryMatches);
            var directory = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (!string.IsNullOrEmpty(directory)) { System.IO.Directory.CreateDirectory(directory); window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(directory, dark ? "dictionary-dark.png" : "dictionary-light.png")); }
        }
        finally { window.Close(); Application.Current.RequestedThemeVariant = previous; }
    }
}
