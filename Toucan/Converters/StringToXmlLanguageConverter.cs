using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Toucan.Converters;

/// <summary>
/// Converts a BCP-47 language code string (e.g. "en-US", "id-ID") to an XmlLanguage
/// for WPF's SpellCheck to use the correct OS language dictionary.
/// </summary>
public class StringToXmlLanguageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string langCode && !string.IsNullOrEmpty(langCode))
        {
            try
            {
                return XmlLanguage.GetLanguage(langCode);
            }
            catch
            {
                // fallback to default if the language tag is invalid
            }
        }
        return XmlLanguage.GetLanguage("en-US");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
