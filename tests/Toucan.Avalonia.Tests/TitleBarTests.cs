using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class TitleBarTests
{
    [Theory]
    [InlineData(true, false, 78, 0)]
    [InlineData(false, false, 0, 138)]
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
        window.Close();
    }
}
