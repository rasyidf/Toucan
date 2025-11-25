using System;
using System.Globalization;
using System.Windows.Data;

namespace Toucan.Converters
{
    public class StringEqualsConverter : IValueConverter
    {
        // parameter = expected value (string). value = current SelectedProvider
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var cur = value as string ?? string.Empty;
            var expected = parameter as string ?? string.Empty;
            return string.Equals(cur, expected, StringComparison.InvariantCultureIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter as string ?? string.Empty;

            return Binding.DoNothing;
        }
    }
}
