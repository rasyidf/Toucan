using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class TitleBarTests
{
    [Theory]
    [InlineData(true, false, 78, 0)]
    [InlineData(false, false, 0, 0)]
    [InlineData(true, true, 0, 0)]
    [InlineData(false, true, 0, 0)]
    public void WindowControlsClearance_MatchesThePlatform(bool mac, bool fullScreen, double leading, double trailing) =>
        Assert.Equal((leading, trailing), PlatformService.TitleBarInsets(mac, fullScreen));

    [AvaloniaFact]
    public void MainWindow_ExtendsIntoTheTitleBarOnEveryPlatform_AndShowsTheBrand()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.True(window.FindControl<StackPanel>("TitleBarBrand")!.IsVisible);
        var (leading, trailing) = PlatformService.TitleBarInsets(PlatformService.IsMacOS, false);
        Assert.Equal(leading, window.FindControl<StackPanel>("TitleBarLeading")!.Margin.Left);
        Assert.Equal(trailing, window.FindControl<StackPanel>("TitleBarTrailing")!.Margin.Right);
        // Windows and Linux draw their own window buttons; macOS keeps the system traffic lights.
        Assert.Equal(!PlatformService.IsMacOS, window.FindControl<StackPanel>("CaptionButtons")!.IsVisible);
        window.Close();
    }

    [Theory]
    [InlineData(1000, 5)]
    [InlineData(300, 2)]   // 40 for "…" + two 100-wide items
    [InlineData(139, 0)]   // not even one item fits beside "…"
    public void ResponsiveMenu_Fit_KeepsWhatFitsAndLeavesRoomForOverflow(double available, int expected) =>
        Assert.Equal(expected, ResponsiveMenu.Fit([100, 100, 100, 100, 100], available));

    [AvaloniaFact]
    public void ResponsiveMenu_FoldsItemsThatDoNotFitIntoAnOverflowMenu()
    {
        var menu = new ResponsiveMenu();
        menu.SetItems(Enumerable.Range(1, 6).Select(i => new MenuItem { Header = $"Menu number {i}" }));
        var host = new ContentControl { Content = menu };
        var window = new Window { Width = 900, Height = 300, Content = host };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(6, menu.ShownCount);

        host.MaxWidth = 260;
        window.UpdateLayout();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.InRange(menu.ShownCount, 1, 5);
        var overflow = menu.Items.OfType<MenuItem>().Last();
        Assert.Equal("…", overflow.Header);
        Assert.Equal(6 - menu.ShownCount, overflow.Items.Count);

        host.MaxWidth = double.PositiveInfinity;
        window.UpdateLayout();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(6, menu.ShownCount);
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), m => Equals(m.Header, "…"));
        window.Close();
    }
}
