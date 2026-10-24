using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Compact headers and wrapping editor controls stay within their layout rows.</summary>
public class PanelAlignmentTests
{
    [AvaloniaFact]
    public async Task PanelHeadersAndEditorToolbar_DoNotOverlapCards()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("align", ("en", """{"app.title": "My App"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();
        await vm.OpenProjectAsync(folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var headers = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panelHeader") && b.IsEffectivelyVisible).ToList();
        var toolbar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "FilterToolbar");

        Assert.NotEmpty(headers);
        // Header and toolbar icon buttons share one compact, rounded size.
        var buttons = headers.Concat([toolbar]).SelectMany(b => b.GetVisualDescendants().OfType<Button>()).Where(b => b.Classes.Contains("icon")).ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, b =>
        {
            Assert.Equal(26, b.Bounds.Width, tolerance: 0.5);
            Assert.Equal(new CornerRadius(6), b.CornerRadius);
        });
        foreach (var header in headers)
        {
            // The filter can occupy two rows at narrow widths; it must grow rather than overlap the first card.
            Assert.True(toolbar.Bounds.Height >= header.Bounds.Height - 0.5);
        }
        var firstCard = window.GetVisualDescendants().OfType<Toucan.Avalonia.Views.Components.TranslationCard>().First(c => c.IsEffectivelyVisible);
        var bottom = toolbar.TranslatePoint(new Point(0, toolbar.Bounds.Height), window)!.Value.Y;
        Assert.True(firstCard.TranslatePoint(default, window)!.Value.Y >= bottom);
    }

    [AvaloniaFact]
    public async Task ModeTabs_HaveNoThemePaintedContentPresenter()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();
        await Task.CompletedTask;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tabs = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("modeTab")).ToList();

        Assert.Equal(3, tabs.Count);
        // The Fluent theme gives #PART_ContentPresenter its own hover background, which showed as a second box inside the tab.
        Assert.All(tabs, t => Assert.DoesNotContain(t.GetVisualDescendants().OfType<ContentPresenter>(), p => p.Name == "PART_ContentPresenter"));
    }

    [AvaloniaFact]
    public void MultiLineTextBoxes_AlignToTop_SingleLineStayCentered()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var multi = new TextBox { AcceptsReturn = true };
        var single = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { multi, single } } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Top, multi.VerticalContentAlignment);
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, single.VerticalContentAlignment);
    }
}
