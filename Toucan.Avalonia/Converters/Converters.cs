using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Toucan.Core.Contracts;

namespace Toucan.Avalonia.Converters;

/// <summary>"fr-FR" → "French (France)". Falls back to the code for custom tags.</summary>
public sealed class LanguageNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string code || string.IsNullOrEmpty(code)) return value;
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch (CultureNotFoundException) { return code; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Icon name string → <see cref="FASymbol"/>.</summary>
public sealed class SymbolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Enum.TryParse<FASymbol>(s, true, out var symbol) ? symbol : FASymbol.Document;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the value's string form equals the converter parameter (used for radio-like toggles).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal static class ThemeBrushes
{
    public static IBrush Get(string key, IBrush fallback)
    {
        var app = Application.Current;
        if (app != null && app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush) return brush;
        return fallback;
    }
}

/// <summary>Validation severity → accent brush.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ValidationSeverity.Error => ThemeBrushes.Get("BadBrush", Brushes.IndianRed),
        ValidationSeverity.Warning => ThemeBrushes.Get("WarnBrush", Brushes.Goldenrod),
        _ => ThemeBrushes.Get("InfoBrush", Brushes.SteelBlue)
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Completion percentage (0–100) → good/warn/bad brush.</summary>
public sealed class PercentToHealthBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            _ => 0
        };
        return percent switch
        {
            >= 95 => ThemeBrushes.Get("GoodBrush", Brushes.SeaGreen),
            >= 70 => ThemeBrushes.Get("WarnBrush", Brushes.Goldenrod),
            _ => ThemeBrushes.Get("BadBrush", Brushes.IndianRed)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Key status ("empty", "approved", "machine", "translated") → brush.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as string) switch
    {
        "empty" => ThemeBrushes.Get("WarnBrush", Brushes.Goldenrod),
        "approved" => ThemeBrushes.Get("GoodBrush", Brushes.SeaGreen),
        "machine" => ThemeBrushes.Get("InfoBrush", Brushes.SteelBlue),
        _ => ThemeBrushes.Get("MutedTextBrush", Brushes.Gray)
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Visibility of a Settings page: the selected one normally, or every page that has matches while searching.
/// Values: selected index, is searching, per-page match flags. Parameter: this page's index.
/// </summary>
public sealed class SettingsPageVisibleConverter : IMultiValueConverter
{
    public static SettingsPageVisibleConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is null || !int.TryParse(parameter.ToString(), CultureInfo.InvariantCulture, out var page)) return false;
        var searching = values.Count > 1 && values[1] is true;
        if (!searching) return values.Count > 0 && values[0] is int selected && selected == page;
        return values.Count > 2 && values[2] is IReadOnlyList<bool> matches && page < matches.Count && matches[page];
    }
}

/// <summary>Collapsed/expanded flag → chevron symbol.</summary>
public sealed class ExpanderChevronConverter : IValueConverter
{
    public static ExpanderChevronConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.ChevronUp : FASymbol.ChevronDown;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
