using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters;

/// <summary>
/// Inverts a boolean value. Useful for disabling controls when a condition is true.
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }
}
