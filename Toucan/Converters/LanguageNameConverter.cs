using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters;

internal class LanguageNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var cult = new CultureInfo(value?.ToString() ?? ""); 
            return cult.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return $"{value} - unsupported";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
