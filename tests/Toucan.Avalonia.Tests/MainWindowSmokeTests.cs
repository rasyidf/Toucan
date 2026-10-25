using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Media;
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
    [AvaloniaTheory]
    [InlineData(1280, 820, false)]
    [InlineData(880, 560, false)]
    [InlineData(1280, 820, true)]
    [InlineData(880, 560, true)]
    public async Task WindowShell_KeepsControlsInsideWindow(int width, int height, bool dark)
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var app = Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>())
        {
            Width = width,
            Height = height
        };
        try
        {
            window.Show();
            CheckShell("start");
            var folder = host.CreateJsonProject("layout", ("en", """{"title": "Hello"}"""), ("de", """{"title": "Hallo"}"""));
            await vm.OpenProjectAsync(folder);
            CheckShell("editor");

            var workspace = window.FindControl<Grid>("Workspace")!;
            Assert.True(workspace.ColumnDefinitions.Sum(c => c.ActualWidth) <= workspace.Bounds.Width + 1, $"Workspace {workspace.Bounds.Width}: {string.Join(", ", workspace.ColumnDefinitions.Select(c => c.ActualWidth))}");
            var editor = window.FindControl<TranslationEditorView>("Editor")!;
            Assert.True(editor.Bounds.Width >= 360);

            var panels = Toucan.Avalonia.Services.PanelService.Instance;
            panels.ToggleSidebarCommand.Execute(null);
            CheckShell("left-collapsed");
            panels.ToggleInspectorCommand.Execute(null);
            CheckShell("both-collapsed");
            panels.ToggleSidebarCommand.Execute(null);
            CheckShell("right-collapsed");
            panels.ToggleInspectorCommand.Execute(null);
            window.WindowState = WindowState.FullScreen;
            CheckShell("fullscreen");
            window.WindowState = WindowState.Normal;
            CheckShell("restored");
            await vm.CloseProjectCommand.ExecuteAsync(null);
            CheckShell("closed");
        }
        finally
        {
            window.Hide();
            app.RequestedThemeVariant = previousTheme;
        }

        void CheckShell(string state)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var content = window.FindControl<Panel>("WorkspaceContent")!;
            var clip = Assert.IsType<RectangleGeometry>(content.Clip);
            Assert.Equal(new Rect(content.Bounds.Size), clip.Rect);
            Assert.Equal(8, clip.RadiusX);
            Assert.Equal(8, clip.RadiusY);
            var rail = window.FindControl<ActivityBar>("LeftActivityBar")!;
            Assert.Equal(!vm.ShowStartScreen, rail.IsEffectivelyVisible);
            Assert.Equal(!vm.ShowStartScreen, window.FindControl<Button>("RailSettings")!.IsEffectivelyVisible);
            if (rail.IsEffectivelyVisible)
            {
                var firstActivity = rail.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("activity"));
                Assert.InRange(firstActivity.TranslatePoint(default, rail)!.Value.Y, 3, 5);
            }
            else
            {
                var surface = window.FindControl<Border>("WorkspaceSurface")!;
                Assert.InRange(surface.TranslatePoint(default, window)!.Value.X, 7, 9);
            }
            var search = window.FindControl<Button>("PalettePill")!;
            var origin = search.TranslatePoint(default, window)!.Value;
            var titleLeading = window.FindControl<Control>("TitleBarLeading")!;
            var titleTrailing = window.FindControl<Control>("TitleBarTrailing")!;
            var leadingEnd = titleLeading.TranslatePoint(new Point(titleLeading.Bounds.Width, 0), window)!.Value.X;
            var trailingStart = titleTrailing.TranslatePoint(default, window)!.Value.X;
            // Search is centered when both sides leave room for it; otherwise it sits between them. Never under either, and the
            // window buttons stay inside the window.
            Assert.True(origin.X >= leadingEnd - 0.5 && origin.X + search.Bounds.Width <= trailingStart + 0.5, $"Search overlaps the title bar sides in {state}");
            Assert.True(trailingStart + titleTrailing.Bounds.Width <= window.ClientSize.Width + 0.5, $"Title bar controls leave the window in {state}");
            if (leadingEnd + 16 <= (window.ClientSize.Width - search.Bounds.Width) / 2 && titleTrailing.Bounds.Width + 16 <= (window.ClientSize.Width - search.Bounds.Width) / 2)
                Assert.InRange(Math.Abs(origin.X + search.Bounds.Width / 2 - window.ClientSize.Width / 2), 0, 1);
            if (vm.HasProject)
            {
                var trailing = window.FindControl<StackPanel>("TitleBarTrailing")!;
                var right = trailing.TranslatePoint(default, window)!.Value.X + trailing.Bounds.Width;
                Assert.InRange(window.ClientSize.Width - right, 7, 9);
                var modes = window.FindControl<StackPanel>("WorkspaceModes")!;
                var modesOrigin = modes.TranslatePoint(default, window)!.Value;
                Assert.True(modes.IsEffectivelyVisible);
                Assert.True(modesOrigin.X >= origin.X + search.Bounds.Width);
                Assert.True(modesOrigin.Y + modes.Bounds.Height <= window.FindControl<Border>("TopBar")!.Bounds.Height);
            }
            foreach (var name in new[] { "PalettePill", "StatusBar" })
            {
                var control = window.FindControl<Control>(name)!;
                Assert.True(control.IsEffectivelyVisible, $"{name} is hidden in {state}");
                var point = control.TranslatePoint(default, window)!.Value;
                Assert.True(point.X >= 0 && point.Y >= 0, name);
                Assert.True(point.X + control.Bounds.Width <= window.ClientSize.Width + 1, name);
                Assert.True(point.Y + control.Bounds.Height <= window.ClientSize.Height + 1, name);
            }
            var shots = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                Directory.CreateDirectory(shots);
                window.CaptureRenderedFrame()!.Save(Path.Combine(shots, $"shell-{width}-{(dark ? "dark" : "light")}-{state}.png"));
            }
        }
    }

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
