using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
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
            Assert.Same(root, backplate.Child);
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
