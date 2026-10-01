using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Loads the real window (all XAML, styles, panels) headlessly and checks it renders a project.</summary>
public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public async Task MainWindow_ShowsStartScreen_ThenEditorCards()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("smoke",
            ("en", """{"app.title": "My App", "buttons.save": "Save"}"""),
            ("de", """{"app.title": "Meine App"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();

        Assert.True(vm.ShowStartScreen);

        await vm.OpenProjectAsync(folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var cards = window.GetVisualDescendants().OfType<TranslationCard>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.Equal(2, cards.Count);
        Assert.Contains("smoke", window.Title, StringComparison.Ordinal);

        // Every panel can be activated without binding or template errors.
        foreach (var id in new[] { "search", "issues", "source-code", "explorer", "inspector", "translation-memory", "languages" })
        {
            Toucan.Core.Services.SidePanelRegistry.Instance.Activate(id);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var shots = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
        if (!string.IsNullOrEmpty(shots))
        {
            Directory.CreateDirectory(shots);
            frame!.Save(Path.Combine(shots, "main-window.png"));
        }
    }
}
