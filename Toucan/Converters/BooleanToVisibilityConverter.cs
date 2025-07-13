using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Toucan.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);

            if (value is bool boolValue)
            {
                boolValue = invert ? !boolValue : boolValue;
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);

            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                return invert ? !result : result;
            }

            return false;
        }
    }
}
