using Toucan.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Toucan.Converters
{
    public class BooleanToThemeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            string defaultTheme = parameter as string;
            bool isDark = defaultTheme == "Dark";

            isDark = isDark && boolValue;
            return isDark ? "Dark" : "Light";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            AppTheme themeValue = (AppTheme)value;
            string defaultTheme = parameter as string;
            bool isDark = defaultTheme == "Dark";

            return themeValue == AppTheme.Dark ? isDark : !isDark;
        }
    }

}
