using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters;
public class ViewModeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "TextBulletListTree20" : "List20";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
