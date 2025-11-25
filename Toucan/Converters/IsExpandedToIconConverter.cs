using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters;

public class IsExpandedToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b)
            ? "ChevronDown16"
            : "ChevronRight16";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}