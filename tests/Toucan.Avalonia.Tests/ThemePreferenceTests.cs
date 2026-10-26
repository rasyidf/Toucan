using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Toucan.Avalonia.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class ThemePreferenceTests
{
    [AvaloniaFact]
    public void LightDarkAndSystemPreferencesRemainAvailable()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        try
        {
            ThemeService.Apply("Dark");
            Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);
            ThemeService.Apply("Light");
            Assert.Equal(ThemeVariant.Light, app.ActualThemeVariant);
            ThemeService.Apply("System");
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
        }
        finally { app.RequestedThemeVariant = original; }
    }
}
