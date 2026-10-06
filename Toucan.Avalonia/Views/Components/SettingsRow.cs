using Avalonia;
using Avalonia.Controls;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// One row of a <see cref="SettingsGroup"/>: title and optional description on the left, the control (content) on the right.
/// Set <see cref="Stacked"/> for wide controls such as text boxes, which then sit under the title at full width.
/// </summary>
public class SettingsRow : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SettingsRow, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsRow, string?>(nameof(Description));
    public static readonly StyledProperty<bool> StackedProperty = AvaloniaProperty.Register<SettingsRow, bool>(nameof(Stacked));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>Minimum height of the row's card line. Kept off <c>MinHeight</c> so a filtered-out row can collapse completely.</summary>
    public static readonly StyledProperty<double> MinRowHeightProperty = AvaloniaProperty.Register<SettingsRow, double>(nameof(MinRowHeight), 44);
    public double MinRowHeight { get => GetValue(MinRowHeightProperty); set => SetValue(MinRowHeightProperty, value); }

    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

    /// <summary>False while the settings search is active and this row does not match it. Collapses the row without touching <c>IsVisible</c>, which pages bind.</summary>
    public static readonly StyledProperty<bool> IsMatchProperty = AvaloniaProperty.Register<SettingsRow, bool>(nameof(IsMatch), true);
    public bool IsMatch { get => GetValue(IsMatchProperty); set => SetValue(IsMatchProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StackedProperty) PseudoClasses.Set(":stacked", Stacked);
    }
}

/// <summary>A titled, rounded card holding <see cref="SettingsRow"/>s separated by hairlines (grouped-list style).</summary>
public class SettingsGroup : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Header));

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>Explanatory text under the card; hidden together with the group.</summary>
    public static readonly StyledProperty<string?> FooterProperty = AvaloniaProperty.Register<SettingsGroup, string?>(nameof(Footer));
    public string? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public static readonly StyledProperty<bool> IsMatchProperty = AvaloniaProperty.Register<SettingsGroup, bool>(nameof(IsMatch), true);
    public bool IsMatch { get => GetValue(IsMatchProperty); set => SetValue(IsMatchProperty, value); }
}
