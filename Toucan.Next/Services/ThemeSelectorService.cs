using System.Windows;

using Toucan.Contracts.Services;
using Toucan.Models;

namespace Toucan.Services;

public class ThemeSelectorService : IThemeSelectorService
{ 

    public ThemeSelectorService()
    {
    }

    public void InitializeTheme()
    { 
        var theme = GetCurrentTheme();
        SetTheme(theme);
    }

    public void SetTheme(AppTheme theme)
    {
        if (theme == AppTheme.Default)
        { 
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
              Wpf.Ui.Appearance.ApplicationTheme.Unknown,     // Theme type
              Wpf.Ui.Controls.WindowBackdropType.Mica, // Background type
              true                                   // Whether to change accents automatically
            );
        }
        else
        { 
            try
            {
                if (SystemParameters.HighContrast)
                {
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                        Wpf.Ui.Appearance.ApplicationTheme.HighContrast,
                        Wpf.Ui.Controls.WindowBackdropType.Mica,
                         true
                     );
                }
                else
                {
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                        theme == AppTheme.Dark
                        ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                        : Wpf.Ui.Appearance.ApplicationTheme.Light,
                         Wpf.Ui.Controls.WindowBackdropType.Mica,
                         true
                   );
                }
            }
            catch (Exception _)
            {

            }
        }

        Application.Current.Properties["Theme"] = theme.ToString();
    }

    public AppTheme GetCurrentTheme()
    {
        if (Application.Current.Properties.Contains("Theme"))
        {
            var themeName = Application.Current.Properties["Theme"].ToString();
            Enum.TryParse(themeName, out AppTheme theme);
            return theme;
        }

        return AppTheme.Default;
    }
}
