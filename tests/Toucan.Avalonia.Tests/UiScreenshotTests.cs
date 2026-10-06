using Avalonia.Controls;
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
        window.Close();
    }

    private sealed class FakeCatalog(params PluginLoadResult[] plugins) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
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

        SnapDialog(new OptionsDialog(PluginsOptions(host)) { Height = 1500 }, "10-options-11-plugins-populated");

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
    public void CaptureDarkTheme()
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
        }
        finally { global::Avalonia.Application.Current!.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Default; }
    }
}
