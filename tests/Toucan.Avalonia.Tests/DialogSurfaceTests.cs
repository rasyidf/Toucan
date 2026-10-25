using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Views.Components;
using Toucan.Avalonia.Views.Dialogs;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class DialogSurfaceTests
{
    [AvaloniaFact]
    public void OpenDialog_BackgroundAndBackplateFollowTheme()
    {
        var app = global::Avalonia.Application.Current!;
        var previous = app.RequestedThemeVariant;
        var root = new DockPanel();
        var dialog = new DialogWindow { Width = 400, Height = 250, Content = root };
        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            dialog.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#F3F3F3"), Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
            var backplate = Assert.IsType<Border>(dialog.Content);
            var layout = Assert.IsType<DockPanel>(backplate.Child);
            Assert.IsType<DialogTitleBar>(layout.Children[0]);
            Assert.Same(root, layout.Children[1]);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#181818"), Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
            Assert.Equal(Color.Parse("#181818"), Assert.IsAssignableFrom<ISolidColorBrush>(backplate.Background).Color);
            Assert.Same(backplate, dialog.Content);
        }
        finally
        {
            dialog.Close();
            app.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedTitleBarTracksTitleResizeStateAndClose(bool resizable)
    {
        var dialog = new DialogWindow { Title = "Dialog title", Width = 500, Height = 300, CanResize = resizable, Content = new TextBox() };
        dialog.Show();
        try
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var titleBar = Assert.Single(dialog.GetVisualDescendants().OfType<DialogTitleBar>());
            Assert.Equal("Dialog title", titleBar.FindControl<TextBlock>("CaptionTitle")!.Text);
            dialog.Title = "Updated title";
            Assert.Equal("Updated title", titleBar.FindControl<TextBlock>("CaptionTitle")!.Text);
            Assert.Equal(40, titleBar.Bounds.Height);
            if (!Toucan.Avalonia.Services.PlatformService.IsMacOS)
            {
                Assert.Equal(WindowDecorations.BorderOnly, dialog.WindowDecorations);
                Assert.False(titleBar.FindControl<Button>("MinimizeButton")!.IsVisible);
                var maximize = titleBar.FindControl<Button>("MaximizeButton")!;
                Assert.Equal(resizable, maximize.IsVisible);
                if (resizable)
                {
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(WindowState.Maximized, dialog.WindowState);
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(WindowState.Normal, dialog.WindowState);
                }
                titleBar.FindControl<Button>("CloseButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(dialog.IsVisible);
            }
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public void ProviderDialogUsesScopeSwitchWithoutDuplicateEditingLabel()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<Toucan.Avalonia.ViewModels.ProviderSettingsViewModel>();
        var dialog = new ProviderSettingsDialog(vm);
        dialog.Show();
        try
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Single(dialog.GetVisualDescendants().OfType<DialogTitleBar>());
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Editing:", StringComparison.Ordinal) == true);
            Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString() == "App-wide");
            Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString() == "Project…");
        }
        finally { dialog.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransparentContentRoot_RendersAnOpaqueClientArea(bool borderRoot)
    {
        using var dialog = new SurfaceWindow();
        dialog.Content = borderRoot ? new Border() : new Grid();
        dialog.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(WindowTransparencyLevel.None, dialog.TransparencyLevelHint);
        using var frame = dialog.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var pixels = frame.Lock();
        // Sample an empty client pixel, away from native decoration and content.
        var pixel = System.Runtime.InteropServices.Marshal.ReadInt32(pixels.Address, pixels.RowBytes * 100 + 100 * 4);
        Assert.Equal(255, (int)((uint)pixel >> 24));
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color;
        Assert.Equal((uint)(color.R << 16 | color.G << 8 | color.B), (uint)pixel & 0xFFFFFF);
    }

    private sealed class SurfaceWindow : DialogWindow, IDisposable
    {
        public SurfaceWindow() { Width = 400; Height = 250; }
        public void Dispose() => Close();
    }
}
