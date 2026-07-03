using System;
using System.Globalization;
using System.Windows.Data;
using Toucan.Core.Models;

namespace Toucan.Converters;

/// <summary>
/// Converts SearchScope enum to/from ComboBox SelectedIndex (0-based int).
/// </summary>
public class SearchScopeToIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is SearchScope scope ? (int)scope : 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int index && Enum.IsDefined(typeof(SearchScope), index)
            ? (SearchScope)index
            : SearchScope.AllLanguages;
    }
}
