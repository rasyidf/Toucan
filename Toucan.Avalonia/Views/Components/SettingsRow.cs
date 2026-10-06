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
    public bool Stacked { get => GetValue(StackedProperty); set => SetValue(StackedProperty, value); }

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
}
