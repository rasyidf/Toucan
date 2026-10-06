using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Models;
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

        SnapDialog(new NewProjectDialog(host.Services.GetRequiredService<NewProjectViewModel>()), "20-new-project");
        SnapDialog(new ImportProjectDialog(new ImportProjectViewModel(host.Services.GetServices<Toucan.Core.Contracts.IFrameworkProfile>(), host.Dialogs)), "21-import-project");
        SnapDialog(new ProjectPropertiesDialog(new ProjectPropertiesViewModel(new ProjectSettings(), host.Dialogs, ["en", "de"])), "22-project-properties");
        SnapDialog(new StatisticsDialog(new StatisticsViewModel([])), "23-statistics");
        SnapDialog(new ManageLanguagesDialog(new LanguageManagerViewModel([], "en")), "24-manage-languages");
        var provider = host.Services.GetRequiredService<ProviderSettingsViewModel>();
        provider.UseProject(null);
        SnapDialog(new ProviderSettingsDialog(provider), "25-provider-settings");
        SnapDialog(new PromptDialog("Rename key", "Enter a new key name", "app.title"), "26-prompt");
        SnapDialog(new PickDialog("Pick language", "Choose one", ["en", "de", "id"], "en"), "27-pick");
        SnapDialog(new LanguagePromptDialog(new LanguagePromptViewModel()), "28-language-prompt");
    }
}
