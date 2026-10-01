using Avalonia;
using Avalonia.Styling;

namespace Toucan.Avalonia.Services;

/// <summary>Applies the user's theme and editor font size preferences.</summary>
internal static class ThemeService
{
    /// <summary>"Light", "Dark", or "System" (follow the OS).</summary>
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = theme?.ToUpperInvariant() switch
        {
            "DARK" => ThemeVariant.Dark,
            "LIGHT" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>Sets the font size used by translation editors (resource key <c>EditorFontSize</c>).</summary>
    public static void ApplyFontSize(double size)
    {
        if (Application.Current is not { } app) return;
        app.Resources["EditorFontSize"] = Math.Clamp(size, 11, 18);
    }
}
