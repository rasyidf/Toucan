using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// A color setting: a swatch that opens a color picker, next to a hex box for typing or pasting a value. Both edit
/// <see cref="Hex"/> (<c>#RRGGBB</c>), so either one updates the other. The color is always opaque.
/// </summary>
public sealed class ColorInput : UserControl
{
    public static readonly StyledProperty<string> HexProperty =
        AvaloniaProperty.Register<ColorInput, string>(nameof(Hex), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Marks the hex box as invalid, for a value the owner could not use.</summary>
    public static readonly StyledProperty<bool> IsInvalidProperty = AvaloniaProperty.Register<ColorInput, bool>(nameof(IsInvalid));

    private readonly Border _chip = new() { Classes = { "colorChip" } };
    private readonly TextBox _box = new() { Classes = { "mono" }, Width = 110, MinWidth = 0, MaxLength = 9 };
    private readonly ColorView _view = new() { IsAlphaVisible = false, IsAlphaEnabled = false, IsColorSpectrumSliderVisible = true, IsComponentSliderVisible = false };
    private bool _syncing;

    public ColorInput()
    {
        var button = new Button
        {
            Classes = { "colorPick" },
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = _chip,
            Flyout = new Flyout { Content = _view, Placement = PlacementMode.BottomEdgeAlignedLeft },
        };
        global::Avalonia.Automation.AutomationProperties.SetName(button, Loc.T("Pick a color"));
        ToolTip.SetTip(button, Loc.T("Pick a color"));
        global::Avalonia.Automation.AutomationProperties.SetName(_box, Loc.T("Color value in hex"));

        _box.TextChanged += (_, _) =>
        {
            if (!_syncing) Hex = _box.Text ?? string.Empty;
        };
        _view.ColorChanged += (_, e) =>
        {
            if (!_syncing) Hex = ColorSchemeService.Hex(e.NewColor);
        };

        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { button, _box } };
        Sync();
    }

    public string Hex { get => GetValue(HexProperty); set => SetValue(HexProperty, value); }

    public bool IsInvalid { get => GetValue(IsInvalidProperty); set => SetValue(IsInvalidProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty) Sync();
        else if (change.Property == IsInvalidProperty) _box.Classes.Set("invalid", IsInvalid);
    }

    /// <summary>Brings the swatch, picker and text box in line with <see cref="Hex"/>. An unparsable value leaves the swatch as it was.</summary>
    private void Sync()
    {
        _syncing = true;
        try
        {
            if (!string.Equals(_box.Text, Hex, StringComparison.Ordinal)) _box.Text = Hex;
            if (ColorSchemeService.TryParseColor(Hex, out var color))
            {
                _chip.Background = new SolidColorBrush(color);
                if (_view.Color != color) _view.Color = color;
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
