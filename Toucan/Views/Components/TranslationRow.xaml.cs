using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Toucan.Views.Components;

/// <summary>
/// A single status row within a language group card (e.g., Translated, Empty, Needs Review, Approved).
/// Shows icon + label + badge count. Click filters by status; context menu offers filter + optional secondary action.
/// </summary>
public partial class TranslationRow : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(SymbolRegular), typeof(TranslationRow), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty IconColorProperty =
        DependencyProperty.Register(nameof(IconColor), typeof(Brush), typeof(TranslationRow));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(TranslationRow));

    public static readonly DependencyProperty BadgeAppearanceProperty =
        DependencyProperty.Register(nameof(BadgeAppearance), typeof(ControlAppearance), typeof(TranslationRow), new PropertyMetadata(ControlAppearance.Secondary));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(TranslationRow));

    public static readonly DependencyProperty FilterStatusKeyProperty =
        DependencyProperty.Register(nameof(FilterStatusKey), typeof(string), typeof(TranslationRow));

    public static readonly DependencyProperty LanguageCodeProperty =
        DependencyProperty.Register(nameof(LanguageCode), typeof(string), typeof(TranslationRow));

    public static readonly DependencyProperty FilterCommandProperty =
        DependencyProperty.Register(nameof(FilterCommand), typeof(ICommand), typeof(TranslationRow));

    public static readonly DependencyProperty SecondaryCommandProperty =
        DependencyProperty.Register(nameof(SecondaryCommand), typeof(ICommand), typeof(TranslationRow));

    public static readonly DependencyProperty SecondaryCommandParameterProperty =
        DependencyProperty.Register(nameof(SecondaryCommandParameter), typeof(object), typeof(TranslationRow));

    public static readonly DependencyProperty SecondaryHeaderProperty =
        DependencyProperty.Register(nameof(SecondaryHeader), typeof(string), typeof(TranslationRow));

    public SymbolRegular Icon { get => (SymbolRegular)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush? IconColor { get => (Brush?)GetValue(IconColorProperty); set => SetValue(IconColorProperty, value); }
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public ControlAppearance BadgeAppearance { get => (ControlAppearance)GetValue(BadgeAppearanceProperty); set => SetValue(BadgeAppearanceProperty, value); }
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public string? FilterStatusKey { get => (string?)GetValue(FilterStatusKeyProperty); set => SetValue(FilterStatusKeyProperty, value); }
    public string? LanguageCode { get => (string?)GetValue(LanguageCodeProperty); set => SetValue(LanguageCodeProperty, value); }
    public ICommand? FilterCommand { get => (ICommand?)GetValue(FilterCommandProperty); set => SetValue(FilterCommandProperty, value); }
    public ICommand? SecondaryCommand { get => (ICommand?)GetValue(SecondaryCommandProperty); set => SetValue(SecondaryCommandProperty, value); }
    public object? SecondaryCommandParameter { get => GetValue(SecondaryCommandParameterProperty); set => SetValue(SecondaryCommandParameterProperty, value); }
    public string? SecondaryHeader { get => (string?)GetValue(SecondaryHeaderProperty); set => SetValue(SecondaryHeaderProperty, value); }

    public TranslationRow() => InitializeComponent();

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        var param = $"{FilterStatusKey}:{LanguageCode}";
        if (FilterCommand?.CanExecute(param) == true)
        {
            FilterCommand.Execute(param);
            e.Handled = true;
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Items.Clear();

        var filterParam = $"{FilterStatusKey}:{LanguageCode}";

        menu.Items.Add(new MenuItem { Header = $"Show {Label}", Command = FilterCommand, CommandParameter = filterParam });

        if (SecondaryCommand is not null && SecondaryHeader is not null)
        {
            menu.Items.Add(new MenuItem { Header = SecondaryHeader, Command = SecondaryCommand, CommandParameter = SecondaryCommandParameter });
        }
    }
}
