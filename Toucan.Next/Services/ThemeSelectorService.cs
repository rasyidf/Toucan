using System.Windows;

using ControlzEx.Theming;

using MahApps.Metro.Theming;

using Toucan.Contracts.Services;
using Toucan.Models;

namespace Toucan.Services;

public class ThemeSelectorService : IThemeSelectorService
{
    private const string HcDarkTheme = "pack://application:,,,/Styles/Themes/HC.Dark.Blue.xaml";
    private const string HcLightTheme = "pack://application:,,,/Styles/Themes/HC.Light.Blue.xaml";

    public ThemeSelectorService()
    {
    }

    public void InitializeTheme()
    {
        //ThemeManager.Current.AddLibraryTheme(new LibraryTheme(new Uri(HcDarkTheme), MahAppsLibraryThemeProvider.DefaultInstance));
        //ThemeManager.Current.AddLibraryTheme(new LibraryTheme(new Uri(HcLightTheme), MahAppsLibraryThemeProvider.DefaultInstance));

        var theme = GetCurrentTheme();
        SetTheme(theme);
    }

    public void SetTheme(AppTheme theme)
    {
        if (theme == AppTheme.Default)
        {
            //ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncAll;
            //ThemeManager.Current.SyncTheme(); 
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
              Wpf.Ui.Appearance.ApplicationTheme.Unknown,     // Theme type
              Wpf.Ui.Controls.WindowBackdropType.Mica, // Background type
              true                                   // Whether to change accents automatically
            );
        }
        else
        {
            //    ThemeManager.Current.ThemeSyncMode = ThemeSyncMode.SyncWithHighContrast;
            //    ThemeManager.Current.SyncTheme();
            //    ThemeManager.Current.ChangeTheme(Application.Current, $"{theme}.Blue", SystemParameters.HighContrast);
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
