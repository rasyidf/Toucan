using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>AI Integration in the app: the app-wide switch, Settings → AI, the prompt editor, onboarding and the secret store.</summary>
public sealed class AiIntegrationUiTests
{
    private static void TurnAi(TestHost host, bool on, string? key = null)
    {
        var store = host.Services.GetRequiredService<IAiSettingsStore>();
        var settings = store.Load();
        settings.Enabled = on;
        store.Save(settings);
        if (key != null) host.Services.GetRequiredService<ISecretService>().SetSecret(SecretKeys.Ai(settings.Backend), key);
        Dispatcher.UIThread.RunJobs();
    }

    // ───────────────────────── The app-wide switch ─────────────────────────

    [AvaloniaFact]
    public void WithAiOff_AiCommandsAreOff_AndTheAiProviderIsNotOffered()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();

        Assert.False(vm.IsAiEnabled);
        Assert.False(vm.AnalyzeTranslationsCommand.CanExecute(null));
        Assert.False(vm.CheckSourceClarityCommand.CanExecute(null));
        Assert.DoesNotContain(vm.ProviderChoices, c => c.Name == "AI");
    }

    [AvaloniaFact]
    public void TurningAiOn_ShowsTheAiProviderAndEnablesTheCommands_AndOffHidesThemAgain()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();

        TurnAi(host, on: true, key: "sk-test");

        Assert.True(vm.IsAiEnabled);
        Assert.True(vm.AnalyzeTranslationsCommand.CanExecute(null));
        var ai = Assert.Single(vm.ProviderChoices, c => c.Name == "AI");
        Assert.True(ai.IsConfigured);

        TurnAi(host, on: false);

        Assert.False(vm.IsAiEnabled);
        Assert.DoesNotContain(vm.ProviderChoices, c => c.Name == "AI");
    }

    [AvaloniaFact]
    public void AnOldProviderChoice_BecomesTheAiProvider()
    {
        using var host = new TestHost();
        TurnAi(host, on: true, key: "sk-test");
        var vm = host.CreateViewModel();
        vm.AppOptions.LastProvider = "Claude";

        vm.RefreshProviderChoices();

        Assert.Equal("AI", vm.SelectedProviderName);
    }

    // ───────────────────────── Settings → AI ─────────────────────────

    [AvaloniaFact]
    public void SettingsSave_StoresTheKeyInTheSecretStore_AndKeepsEachServicesValues()
    {
        using var host = new TestHost();
        var options = host.Services.GetRequiredService<OptionsViewModel>();
        var ai = options.Ai!;

        ai.Enabled = true;
        ai.SelectedBackend = ai.Backends.Single(b => b.Id == "anthropic");
        ai.ApiKey = "sk-ant";
        ai.Model = "claude-sonnet";
        ai.SelectedBackend = ai.Backends.Single(b => b.Id == "openai");
        Assert.Equal(string.Empty, ai.ApiKey);
        ai.Endpoint = "http://localhost:11434/v1";
        ai.SelectedBackend = ai.Backends.Single(b => b.Id == "anthropic");
        Assert.Equal(("sk-ant", "claude-sonnet"), (ai.ApiKey, ai.Model));
        ai.Features.Single(f => f.Id == AiFeatureIds.Clarity).Enabled = false;

        options.SaveCommand.Execute(null);

        var settings = host.Services.GetRequiredService<IAiSettingsStore>().Load();
        Assert.True(settings.Enabled);
        Assert.Equal("anthropic", settings.Backend);
        Assert.Equal("claude-sonnet", settings.BackendSettings("anthropic").Model);
        Assert.Equal("http://localhost:11434/v1", settings.BackendSettings("openai").Endpoint);
        Assert.False(settings.FeatureSettings(AiFeatureIds.Clarity).Enabled);
        Assert.Equal("sk-ant", host.Services.GetRequiredService<ISecretService>().GetSecret("ai/anthropic/api_key"));
        Assert.DoesNotContain("sk-ant", File.ReadAllText(Path.Combine(host.Root, "ai.json")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void CancellingSettings_ChangesNothing()
    {
        using var host = new TestHost();
        var options = host.Services.GetRequiredService<OptionsViewModel>();
        options.Ai!.Enabled = true;
        options.Ai.ApiKey = "sk-ant";

        options.CancelCommand.Execute(null);

        Assert.False(host.Services.GetRequiredService<IAiSettingsStore>().Load().Enabled);
        Assert.Empty(host.Services.GetRequiredService<ISecretService>().Keys());
    }

    [AvaloniaFact]
    public void AiPageRenders()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OptionsViewModel>();
        vm.SelectedPageIndex = OptionsViewModel.AiPage;
        Assert.Equal("AI", OptionsViewModel.Pages[OptionsViewModel.AiPage]);

        var dialog = new OptionsDialog(vm);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog.CaptureRenderedFrame());
        dialog.Close();
    }

    // ───────────────────────── Prompt editor ─────────────────────────

    [AvaloniaFact]
    public async Task EditingAPrompt_SavesTheUsersVersion_AndTheFeatureShowsIt()
    {
        using var host = new TestHost();
        var options = host.Services.GetRequiredService<OptionsViewModel>();
        var translate = options.Ai!.Features.Single(f => f.Id == AiFeatureIds.Translate);
        Assert.False(translate.IsCustomized);
        host.Dialogs.OnPromptEditor = editor =>
        {
            Assert.False(editor.HasProject);
            Assert.True(editor.IsDefault);
            Assert.Contains(editor.Variables, v => v.Token == "{{target_language}}");
            editor.Text = "Translate to {{target_language}} like a pirate.";
            editor.SaveCommand.Execute(null);
        };

        await translate.EditPromptCommand.ExecuteAsync(null);

        Assert.True(translate.IsCustomized);
        Assert.Equal("Your prompt", translate.PromptSourceText);
        Assert.Equal("Translate to {{target_language}} like a pirate.\n", File.ReadAllText(Path.Combine(host.Root, "prompts", "translate.md")));
    }

    [AvaloniaFact]
    public void ThePromptEditor_EditsTheProjectsVersionSeparately()
    {
        using var host = new TestHost();
        var prompts = host.Services.GetRequiredService<IPromptLibrary>();
        var project = Directory.CreateDirectory(Path.Combine(host.Root, "proj")).FullName;
        prompts.Save(AiFeatureIds.Analyze, "Mine", PromptSource.User);

        var editor = new PromptEditorViewModel(prompts, prompts.GetFeature(AiFeatureIds.Analyze)!, project);
        Assert.False(editor.ProjectScope);
        Assert.Equal("Mine", editor.Text);

        editor.UseThisProjectCommand.Execute(null);
        Assert.Equal("Mine", editor.Text); // inherited from the user's version until the project has its own
        editor.Text = "Project review";
        editor.SaveCommand.Execute(null);

        Assert.Equal("Project review", prompts.GetPrompt(AiFeatureIds.Analyze, project).Text);
        Assert.Equal("Mine", prompts.GetPrompt(AiFeatureIds.Analyze).Text);

        var again = new PromptEditorViewModel(prompts, prompts.GetFeature(AiFeatureIds.Analyze)!, project);
        Assert.True(again.ProjectScope);
        again.RemoveOverrideCommand.Execute(null);
        Assert.Equal(PromptSource.User, prompts.GetPrompt(AiFeatureIds.Analyze, project).Source);
    }

    [AvaloniaFact]
    public void PromptEditorDialogRenders()
    {
        using var host = new TestHost();
        var prompts = host.Services.GetRequiredService<IPromptLibrary>();
        var dialog = new PromptEditorDialog(new PromptEditorViewModel(prompts, prompts.GetFeature(AiFeatureIds.Clarity)!, host.Root));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog.CaptureRenderedFrame());
        dialog.Close();
    }

    // ───────────────────────── Onboarding ─────────────────────────

    [AvaloniaFact]
    public async Task Onboarding_IsShownOnce()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        vm.AppOptions.OnboardingVersion = 0;

        await vm.RunOnboardingIfNeededAsync();
        await vm.RunOnboardingIfNeededAsync();

        Assert.Equal(1, host.Dialogs.OnboardingShown);
        Assert.Equal(OnboardingViewModel.CurrentVersion, vm.AppOptions.OnboardingVersion);
    }

    [AvaloniaFact]
    public void Onboarding_KeepsAiOffUnlessTheUserTurnsItOn()
    {
        using var host = new TestHost();
        var onboarding = host.Services.GetRequiredService<OnboardingViewModel>();
        Assert.False(onboarding.UseAi);
        Assert.True(onboarding.KeepAiOff);

        onboarding.FinishCommand.Execute(null);
        Assert.False(host.Services.GetRequiredService<IAiSettingsStore>().Load().Enabled);

        var again = host.Services.GetRequiredService<OnboardingViewModel>();
        again.UseAi = true;
        again.Ai.ApiKey = "sk-from-onboarding";
        again.FinishCommand.Execute(null);

        Assert.True(host.Services.GetRequiredService<IAiSettingsStore>().Load().Enabled);
        Assert.Equal("sk-from-onboarding", host.Services.GetRequiredService<ISecretService>().GetSecret("ai/anthropic/api_key"));
    }

    [AvaloniaFact]
    public void OnboardingDialogRenders_AndClosingItKeepsAiOff()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OnboardingViewModel>();
        vm.UseAi = true;
        var dialog = new OnboardingDialog(vm);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(dialog.CaptureRenderedFrame());

        dialog.Close();

        Assert.False(host.Services.GetRequiredService<IAiSettingsStore>().Load().Enabled);
        Assert.True(File.Exists(Path.Combine(host.Root, "ai.json")));
    }

    // ───────────────────────── Secrets ─────────────────────────

    [AvaloniaFact]
    public void ProviderKeys_GoToTheSecretStore_NotToProvidersJson()
    {
        using var host = new TestHost();
        var service = host.Services.GetRequiredService<IProviderSettingsService>();
        var project = Directory.CreateDirectory(Path.Combine(host.Root, "proj")).FullName;

        service.SaveAppProviderSettings([new ProviderSettings { Provider = "DeepL", Options = new() { ["endpoint"] = "e" }, Secrets = new() { ["api_key"] = "deepl-key" } }]);
        service.SaveProjectProviderSettings(project, [new ProviderSettings { Provider = "DeepL", Secrets = new() { ["api_key"] = "project-key" } }]);

        Assert.DoesNotContain("deepl-key", File.ReadAllText(Path.Combine(host.Root, "providers.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("project-key", File.ReadAllText(Path.Combine(project, ".toucan", "providers.json")), StringComparison.Ordinal);
        Assert.Equal("deepl-key", service.LoadAppProviderSettings().Single().Secrets["api_key"]);
        Assert.Equal("project-key", service.LoadProjectProviderSettings(project).Single().Secrets["api_key"]);

        // Removing the provider removes its key too.
        service.SaveAppProviderSettings([]);
        Assert.Null(host.Services.GetRequiredService<ISecretService>().GetSecret("mt/deepl/api_key"));
        Assert.Equal("project-key", service.LoadProjectProviderSettings(project).Single().Secrets["api_key"]);
    }

    [AvaloniaFact]
    public void InlineKeysFromOlderVersions_StillWork_AndMoveToTheSecretStoreOnSave()
    {
        using var host = new TestHost();
        var protector = host.Services.GetRequiredService<ISecureStorageService>();
        var file = Path.Combine(host.Root, "providers.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new[] { new ProviderSettings { Provider = "DeepL", Secrets = new() { ["api_key"] = protector.Protect("old-key") } } }));
        var service = host.Services.GetRequiredService<IProviderSettingsService>();

        var loaded = service.LoadAppProviderSettings().ToList();
        Assert.Equal("old-key", loaded.Single().Secrets["api_key"]);

        service.SaveAppProviderSettings(loaded);

        Assert.Equal("old-key", host.Services.GetRequiredService<ISecretService>().GetSecret("mt/deepl/api_key"));
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(string.Empty, json.RootElement[0].GetProperty("Secrets").GetProperty("api_key").GetString());
    }

    [AvaloniaFact]
    public async Task DataAndPrivacy_ListsSecretNames_AndRemovesThem()
    {
        using var host = new TestHost();
        var secrets = host.Services.GetRequiredService<ISecretService>();
        secrets.SetSecret("ai/anthropic/api_key", "a");
        secrets.SetSecret("mt/deepl/api_key", "b");
        var options = host.Services.GetRequiredService<OptionsViewModel>();

        Assert.Equal(["ai/anthropic/api_key", "mt/deepl/api_key"], options.StoredSecrets);
        options.RemoveSecretCommand.Execute("mt/deepl/api_key");
        Assert.Equal(["ai/anthropic/api_key"], options.StoredSecrets);

        host.Messages.ConfirmAnswer = true;
        await options.RemoveAllSecretsCommand.ExecuteAsync(null);
        Assert.Empty(secrets.Keys());
        Assert.True(options.HasNoStoredSecrets);
    }
}
