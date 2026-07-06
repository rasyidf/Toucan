using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters;

/// <summary>
/// Converts bool to opacity: true = 1.0, false = 0.5.
/// Used for toggle buttons to show active/inactive state.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
