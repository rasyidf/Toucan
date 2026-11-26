using Avalonia;

namespace Toucan.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        args is ["--self-check"] ? SelfCheck.Run(Console.Out) : BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
