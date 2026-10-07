using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;
using Toucan.Avalonia.Locales;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// An editable table in a settings card: column titles, one row per item with borderless cells, and an add row.
/// Items need a <c>Value</c> property; with <see cref="ShowKey"/> they also need <c>Key</c>, and may have <c>Hint</c> (placeholder)
/// and <c>IsSchemaField</c> (read-only key that cannot be removed). Use it for every inline table so they all look the same.
/// </summary>
public class EditableTable : SettingsList
{
    public static readonly StyledProperty<bool> ShowKeyProperty = AvaloniaProperty.Register<EditableTable, bool>(nameof(ShowKey), true);
    public static readonly StyledProperty<bool> IsSecretProperty = AvaloniaProperty.Register<EditableTable, bool>(nameof(IsSecret));
    public static readonly StyledProperty<string?> KeyHeaderProperty = AvaloniaProperty.Register<EditableTable, string?>(nameof(KeyHeader));
    public static readonly StyledProperty<string?> ValueHeaderProperty = AvaloniaProperty.Register<EditableTable, string?>(nameof(ValueHeader));
    public static readonly StyledProperty<ICommand?> RemoveCommandProperty = AvaloniaProperty.Register<EditableTable, ICommand?>(nameof(RemoveCommand));
    public static readonly StyledProperty<string?> RemoveTipProperty = AvaloniaProperty.Register<EditableTable, string?>(nameof(RemoveTip));

    /// <summary>Show a key column before the value column. Off for a plain list of values.</summary>
    public bool ShowKey { get => GetValue(ShowKeyProperty); set => SetValue(ShowKeyProperty, value); }
    /// <summary>Mask the value cells (with a reveal button).</summary>
    public bool IsSecret { get => GetValue(IsSecretProperty); set => SetValue(IsSecretProperty, value); }
    public string? KeyHeader { get => GetValue(KeyHeaderProperty); set => SetValue(KeyHeaderProperty, value); }
    public string? ValueHeader { get => GetValue(ValueHeaderProperty); set => SetValue(ValueHeaderProperty, value); }
    /// <summary>Called with the row's item when its remove button is pressed.</summary>
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public string? RemoveTip { get => GetValue(RemoveTipProperty); set => SetValue(RemoveTipProperty, value); }

    private const double KeyColumnWidth = 150;
    private const double RemoveColumnWidth = 28;

    /// <summary>Reuse the SettingsList template and styles.</summary>
    protected override Type StyleKeyOverride => typeof(SettingsList);

    public EditableTable()
    {
        ShowSearch = false;
        ItemTemplate = new FuncDataTemplate<object>((_, _) => BuildRow(), supportsRecycling: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowKeyProperty || change.Property == KeyHeaderProperty || change.Property == ValueHeaderProperty)
            ColumnHeader = BuildHeader();
    }

    private Control? BuildHeader()
    {
        if (string.IsNullOrEmpty(ValueHeader) && string.IsNullOrEmpty(KeyHeader)) return null;
        var grid = Columns();
        var col = 0;
        if (ShowKey)
        {
            grid.Children.Add(Title(KeyHeader, col));
            col = 2;
        }
        grid.Children.Add(Title(ValueHeader, col));
        return grid;

        static TextBlock Title(string? text, int column)
        {
            var t = new TextBlock { Text = string.IsNullOrEmpty(text) ? null : Loc.T(text), Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(t, column);
            return t;
        }
    }

    private Grid Columns()
    {
        var grid = new Grid();
        if (ShowKey)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(KeyColumnWidth, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Pixel));
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(RemoveColumnWidth + 6, GridUnitType.Pixel));
        return grid;
    }

    private Control BuildRow()
    {
        var grid = Columns();
        var valueColumn = 0;

        if (ShowKey)
        {
            var key = new TextBox { Classes = { "cell", "mono" } };
            key.Bind(TextBox.TextProperty, new Binding("Key") { Mode = BindingMode.TwoWay });
            key.Bind(TextBox.IsReadOnlyProperty, new Binding("IsSchemaField"));
            grid.Children.Add(key);

            var divider = new Border { Width = 1, Margin = new Thickness(0, 4), Opacity = 0.6 };
            divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("SettingsDividerBrush"));
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);
            valueColumn = 2;
        }

        var value = new TextBox { Classes = { "cell" } };
        value.Bind(TextBox.TextProperty, new Binding("Value") { Mode = BindingMode.TwoWay });
        value.Bind(TextBox.PlaceholderTextProperty, new Binding("Hint"));
        if (!ShowKey) value.Classes.Add("mono");
        if (IsSecret)
        {
            value.PasswordChar = '•';
            value.Classes.Add("revealPasswordButton");
        }
        Grid.SetColumn(value, valueColumn);
        grid.Children.Add(value);

        var remove = new Button
        {
            Classes = { "icon", "small" },
            Content = new FASymbolIcon { Symbol = FASymbol.Dismiss },
            Width = RemoveColumnWidth,
            Height = RemoveColumnWidth,
            Margin = new Thickness(6, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Bind(Button.CommandProperty, new Binding(nameof(RemoveCommand)) { Source = this });
        remove.Bind(Button.CommandParameterProperty, new Binding());
        remove.Bind(ToolTip.TipProperty, new Binding(nameof(RemoveTip)) { Source = this });
        if (ShowKey) remove.Bind(IsVisibleProperty, new Binding("!IsSchemaField"));
        Grid.SetColumn(remove, valueColumn + 1);
        grid.Children.Add(remove);

        return new SettingsRow { Compact = true, Stacked = true, Content = grid };
    }
}
