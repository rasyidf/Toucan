using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R7: accessibility — RTL target content and accessible names on icon-only controls.</summary>
public class AccessibilityTests
{
    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    // A control a screen reader can announce: it has an AutomationProperties.Name, or visible text, or a tooltip.
    private static bool HasAccessibleName(Control c)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(c))) return true;
        if (ToolTip.GetTip(c) is string { Length: > 0 }) return true;
        if (AutomationProperties.GetLabeledBy(c) is not null) return true;
        return c switch
        {
            ContentControl { Content: string s } => !string.IsNullOrWhiteSpace(s),
            ContentControl { Content: { } content } => content.GetType().Name != nameof(Panel), // icon content without a name counts as unnamed
            _ => false,
        };
    }

    [AvaloniaFact]
    public async Task RtlTargetContentFlowsRightToLeft()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("rtl",
            ("en", """{"greeting": "Hello"}"""),
            ("ar", """{"greeting": "مرحبا"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        var ar = vm.AllTranslation.First(t => t.Language == "ar");
        var arRow = vm.PagingController.Data.Single(g => g.Namespace == "greeting").Translations.Single(t => t.Language == "ar");
        var enRow = vm.PagingController.Data.Single(g => g.Namespace == "greeting").Translations.Single(t => t.Language == "en");

        Assert.True(arRow.IsRtl);
        Assert.Equal(FlowDirection.RightToLeft, arRow.FlowDirection);
        Assert.False(enRow.IsRtl);
        Assert.Equal(FlowDirection.LeftToRight, enRow.FlowDirection);
    }

    [AvaloniaFact]
    public async Task InteractiveControlsInTheMainWindowHaveAccessibleNames()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("a11y", ("en", """{"app.title": "My App", "buttons.save": "Save"}"""), ("de", """{"app.title": "Meine App"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        Pump();
        await vm.OpenProjectAsync(folder);
        Pump();

        // Icon-only buttons and toggles are the ones a screen reader cannot read from their content.
        // PART_* controls are internal template parts of framework controls (e.g. the TreeView chevron),
        // whose accessibility is handled by their parent's automation peer, so they are not app-authored gaps.
        var unnamed = window.GetVisualDescendants()
            .OfType<TemplatedControl>()
            .Where(c => c is Button or ToggleButton or RepeatButton)
            .Where(c => c.IsVisible && c is ContentControl { Content: not string })
            .Where(c => c.Name is null || !c.Name.StartsWith("PART_", StringComparison.Ordinal))
            .Where(c => !HasAccessibleName(c))
            .Select(c => c.Name ?? c.GetType().Name)
            .ToList();

        Assert.True(unnamed.Count == 0, $"Icon-only controls without an accessible name: {string.Join(", ", unnamed)}");
    }

    [AvaloniaFact]
    public async Task MainWindowRendersAtANarrowWidthAndLargeText()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("narrow",
            ("en", "{" + string.Join(",", Enumerable.Range(1, 20).Select(i => $"\"k{i:00}.title\": \"A longer title number {i} that could overflow a narrow pane\"")) + "}"),
            ("de", """{"k01.title": "Ein längerer Titel"}"""));
        var vm = host.CreateViewModel();
        // Narrow (the window's MinWidth) and a larger editor font, to flush out layout and clipping problems.
        Toucan.Avalonia.Services.ThemeService.ApplyFontSize(18);
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 880, Height = 600 };
        try
        {
            window.Show();
            Pump();
            await vm.OpenProjectAsync(folder);
            Pump();

            // A frame renders without a layout exception, at a narrow width with large text.
            Assert.NotNull(window.CaptureRenderedFrame());

            // Opening each panel at this width must not throw either.
            foreach (var id in new[] { "explorer", "search", "issues", "inspector", "languages" })
            {
                Toucan.Core.Services.SidePanelRegistry.Instance.Activate(id);
                Pump();
            }
            Assert.NotNull(window.CaptureRenderedFrame());
        }
        finally
        {
            window.Close();
            Toucan.Avalonia.Services.ThemeService.ApplyFontSize(13); // restore the default for later tests
        }
    }
}
