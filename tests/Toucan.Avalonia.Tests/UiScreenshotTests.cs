using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>
/// Renders every window, panel and dialog headlessly and saves PNGs for visual review.
/// Only writes files when TOUCAN_TEST_SCREENSHOTS names an output folder (see docs/visual-review.md).
/// </summary>
public class UiScreenshotTests
{
    private static readonly string? OutDir = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");

    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static void Snap(Window window, string name)
    {
        if (string.IsNullOrEmpty(OutDir)) return;
        Directory.CreateDirectory(OutDir);
        Pump();
        window.CaptureRenderedFrame()?.Save(Path.Combine(OutDir, name + ".png"));
    }

    private static void SnapDialog(Window dialog, string name)
    {
        dialog.Show();
        Pump();
        Snap(dialog, name);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task CaptureMainWindowAndPanels()
    {
        if (string.IsNullOrEmpty(OutDir)) return;
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("shots",
            ("en", "{" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"\"k{i:00}.title\": \"Title {i}\"")) + ", \"app.title\": \"My App\"}"),
            ("de", """{"app.title": "Meine App", "buttons.save": "Speichern"}"""),
            ("id", """{"app.title": "Aplikasiku"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        Pump();
        Snap(window, "01-start-screen");

        await vm.OpenProjectAsync(folder);
        Pump();
        Snap(window, "02-editor");

        foreach (var id in new[] { "search", "issues", "source-code", "explorer", "inspector", "translation-memory", "languages", "machine-translation" })
        {
            Toucan.Core.Services.SidePanelRegistry.Instance.Activate(id);
            Pump();
            Snap(window, "03-panel-" + id);
        }

        if (window.GetVisualDescendants().OfType<Toucan.Avalonia.Views.Components.TranslationCard>().FirstOrDefault()?.DataContext is LanguageGroupViewModel group)
            vm.SelectedGroup = group;
        Toucan.Core.Services.SidePanelRegistry.Instance.Activate("inspector");
        Pump();
        Snap(window, "06-inspector");
        vm.IsDirty = true;
        Pump();
        Snap(window, "09-titlebar-unsaved");
        vm.ToggleCommandPaletteCommand.Execute(null);
        Pump();
        Snap(window, "10-palette-open");
        window.GetVisualDescendants().OfType<TextBox>().First(t => t.Classes.Contains("paletteInput")).Text = "save";
        Pump();
        Snap(window, "11-palette-query");
        vm.IsCommandPaletteOpen = false;
        vm.IsDirty = false;
        Pump();
        vm.ToggleShortcutSheetCommand.Execute(null);
        Pump();
        Snap(window, "12-shortcut-sheet");
        vm.IsShortcutSheetOpen = false;
        if (window.GetVisualDescendants().OfType<Toucan.Avalonia.Views.Components.TranslationCard>().FirstOrDefault()?.DataContext is LanguageGroupViewModel ghostGroup
            && ghostGroup.Translations.LastOrDefault() is { } ghostItem)
        {
            var original = ghostItem.Value;
            ghostItem.Value = string.Empty;
            ghostItem.GhostText = "Suggestion from translation memory";
            Pump();
            Snap(window, "13-ghost-text");
            ghostItem.GhostText = null;
            ghostItem.Value = original;
            Pump();
        }
        vm.ToggleZenModeCommand.Execute(null);
        window.GetVisualDescendants().OfType<Toucan.Avalonia.Views.ZenEditorView>().First().SetTitleBarInset(78);
        Pump();
        Snap(window, "08-zen-mode");
        vm.ToggleZenModeCommand.Execute(null);
        Pump();
        vm.IsMultiSelectMode = true;
        Pump();
        Snap(window, "07-multiselect-toolbar");
        vm.IsMultiSelectMode = false;
        vm.HideNamespaceCommand.Execute("buttons");
        Pump();
        Toucan.Core.Services.SidePanelRegistry.Instance.Activate("explorer");
        Pump();
        var toggle = window.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(t => t.Name == "HiddenToggle");
        Snap(window, "04-hidden-namespaces-collapsed");
        if (toggle is not null) { toggle.IsChecked = true; Snap(window, "05-hidden-namespaces-expanded"); }
        window.Close();
    }

    private sealed class FakeCatalog(IReadOnlyList<BuiltInModuleInfo> modules, params PluginLoadResult[] plugins) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
        public IReadOnlyList<BuiltInModuleInfo> BuiltInModules { get; } = modules;
    }

    /// <summary>Settings → Plugins filled with the TestPlugins fixture in every state a plugin can be in.</summary>
    private static OptionsViewModel PluginsOptions(TestHost host)
    {
        const string hash = "8a2d9b1f0700b11098685a79cc6054541e20d6b302295fc3e4cc3946bb366b1c";
        PluginLoadResult Result(string id, string name, string description, PluginStatus status, PluginTrustState? trust = null, string? error = null, params string[] provides) =>
            new(Path.Combine(host.Root, "plugins", id), status,
                new PluginManifest { Id = id, Name = name, Version = "1.2.0", ApiVersion = "1.0", EntryAssembly = "Toucan.TestPlugins.dll", Author = "Toucan", Description = description },
                error, provides.Length > 0 ? provides : null, hash, Trust: trust);

        var policy = new FilePluginPolicyStore(Path.Combine(host.Root, "policy.json"));
        var catalog = new FakeCatalog(
            host.Services.GetRequiredService<IPluginCatalog>().BuiltInModules,
            Result("toucan.test.format", "Test format", "Reads and writes simple key=value files.", PluginStatus.Loaded, PluginTrustState.Trusted, null, "format:test-fmt", "profile:test-profile"),
            Result("toucan.test.rule", "Test rule", "Flags values equal to \"forbidden\".", PluginStatus.NeedsTrust, PluginTrustState.Untrusted, null, "rule:test.rule"),
            Result("toucan.test.provider", "Test MT", "Fixture machine translation provider.", PluginStatus.NeedsTrust, PluginTrustState.Changed, null, "provider:TestMt"),
            Result("toucan.test.off", "Disabled plugin", "Switched off by the user.", PluginStatus.Disabled),
            Result("toucan.test.bad", "Broken plugin", "Fails during startup.", PluginStatus.Failed, null, "Could not load assembly 'Broken.dll'."));
        var vm = new OptionsViewModel(host.Services.GetRequiredService<IPreferenceService>(), host.Services.GetRequiredService<IProjectDefaultsService>(),
            host.Dialogs, host.Messages, pluginCatalog: catalog, pluginPolicy: policy);
        vm.SelectedPageIndex = OptionsViewModel.PluginsPage;
        return vm;
    }

    [AvaloniaFact]
    public void CaptureDialogs()
    {
        if (string.IsNullOrEmpty(OutDir)) return;
        using var host = new TestHost();
        App.RegisterSidePanels();

        for (var i = 0; i < OptionsViewModel.Pages.Count; i++)
        {
            var vm = host.Services.GetRequiredService<OptionsViewModel>();
            vm.SelectedPageIndex = i;
            SnapDialog(new OptionsDialog(vm), $"10-options-{i:00}-{OptionsViewModel.Pages[i].ToLowerInvariant().Replace(' ', '-').Replace("&", "and")}");
        }

        foreach (var (query, name) in new[] { ("valid", "40-search-valid"), ("lang", "41-search-lang"), ("zzz", "42-search-none") })
        {
            var searchVm = host.Services.GetRequiredService<OptionsViewModel>();
            var searchDialog = new OptionsDialog(searchVm);
            searchDialog.Show();
            Pump();
            searchVm.SearchText = query;
            Snap(searchDialog, name);
            searchDialog.Close();
        }

        SnapDialog(new OptionsDialog(PluginsOptions(host)) { Height = 2400 }, "10-options-12-plugins-populated");

        // Settings → AI with AI turned on and a prompt customized, the prompt editor, and first-run onboarding.
        var prompts = host.Services.GetRequiredService<IPromptLibrary>();
        prompts.Save(AiFeatureIds.Clarity, "My clarity prompt", PromptSource.User);
        var aiOn = host.Services.GetRequiredService<OptionsViewModel>();
        aiOn.Ai!.Enabled = true;
        aiOn.Ai.ApiKey = "sk-ant-example";
        aiOn.SelectedPageIndex = OptionsViewModel.AiPage;
        SnapDialog(new OptionsDialog(aiOn) { Height = 1100 }, "10-options-04-ai-on");
        prompts.Reset(AiFeatureIds.Clarity, PromptSource.User);
        SnapDialog(new PromptEditorDialog(new PromptEditorViewModel(prompts, prompts.GetFeature(AiFeatureIds.Translate)!, host.Root)), "29-prompt-editor");
        var onboarding = host.Services.GetRequiredService<OnboardingViewModel>();
        onboarding.UseAi = true;
        SnapDialog(new OnboardingDialog(onboarding), "29-onboarding");

        SnapDialog(new NewProjectDialog(host.Services.GetRequiredService<NewProjectViewModel>()), "20-new-project");
        SnapDialog(new ImportProjectDialog(new ImportProjectViewModel(host.Services.GetServices<Toucan.Core.Contracts.IFrameworkProfile>(), host.Dialogs)), "21-import-project");
        for (var i = 0; i < ProjectPropertiesViewModel.NavEntries.Count; i++)
        {
            var dialog = new ProjectPropertiesDialog(new ProjectPropertiesViewModel(new ProjectSettings(), host.Dialogs, ["en", "de"]));
            dialog.Show();
            Pump();
            dialog.FindControl<ListBox>("Nav")!.SelectedIndex = i;
            Snap(dialog, $"22-project-properties-{i}");
            dialog.Close();
        }
        SnapDialog(new StatisticsDialog(new StatisticsViewModel([])), "23-statistics");
        SnapDialog(new ManageLanguagesDialog(new LanguageManagerViewModel([], "en")), "24-manage-languages");
        var provider = host.Services.GetRequiredService<ProviderSettingsViewModel>();
        provider.UseProject(null);
        SnapDialog(new ProviderSettingsDialog(provider), "25-provider-settings");
        SnapDialog(new PromptDialog("Rename key", "Enter a new key name", "app.title"), "26-prompt");
        SnapDialog(new PickDialog("Pick language", "Choose one", ["en", "de", "id"], "en"), "27-pick");
        SnapDialog(new LanguagePromptDialog(new LanguagePromptViewModel()), "28-language-prompt");
    }

    [AvaloniaFact]
    public async Task CaptureDarkTheme()
    {
        if (string.IsNullOrEmpty(OutDir)) return;
        using var host = new TestHost();
        App.RegisterSidePanels();
        global::Avalonia.Application.Current!.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        try
        {
            foreach (var i in new[] { 0, 4, 8 })
            {
                var vm = host.Services.GetRequiredService<OptionsViewModel>();
                vm.SelectedPageIndex = i;
                SnapDialog(new OptionsDialog(vm), $"30-dark-options-{i:00}");
            }
            var dialog = new ProjectPropertiesDialog(new ProjectPropertiesViewModel(new ProjectSettings(), host.Dialogs, ["en", "de"]));
            dialog.Show();
            Pump();
            dialog.FindControl<ListBox>("Nav")!.SelectedIndex = 1;
            Snap(dialog, "31-dark-project-properties");
            dialog.Close();

            SnapDialog(new NewProjectDialog(host.Services.GetRequiredService<NewProjectViewModel>()), "32-dark-new-project");
            SnapDialog(new ImportProjectDialog(new ImportProjectViewModel(host.Services.GetServices<IFrameworkProfile>(), host.Dialogs)), "33-dark-import");
            SnapDialog(new StatisticsDialog(new StatisticsViewModel([])), "34-dark-statistics");
            var provider = host.Services.GetRequiredService<ProviderSettingsViewModel>();
            provider.UseProject(null);
            SnapDialog(new ProviderSettingsDialog(provider), "35-dark-provider-settings");

            var folder = host.CreateJsonProject("dark",
                ("en", "{" + string.Join(",", Enumerable.Range(1, 40).Select(i => $"\"k{i:00}.title\": \"Title {i}\"")) + ", \"app.title\": \"My App\"}"),
                ("de", """{"app.title": "Meine App"}"""));
            var mainVm = host.CreateViewModel();
            var window = new MainWindow(mainVm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
            window.Show();
            Pump();
            Snap(window, "36-dark-start-screen");
            await mainVm.OpenProjectAsync(folder);
            Pump();
            Snap(window, "37-dark-editor");

            if (window.GetVisualDescendants().OfType<Toucan.Avalonia.Views.Components.TranslationCard>().FirstOrDefault()?.DataContext is LanguageGroupViewModel group)
                mainVm.SelectedGroup = group;
            foreach (var id in new[] { "explorer", "search", "issues", "source-code", "languages", "inspector", "machine-translation", "translation-memory" })
            {
                Toucan.Core.Services.SidePanelRegistry.Instance.Activate(id);
                Pump();
                Snap(window, "38-dark-panel-" + id);
            }
            window.Close();
        }
        finally { global::Avalonia.Application.Current!.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Default; }
    }
}
