using System.Windows;
using System.Windows.Controls;

namespace Toucan.Views.Components;

public partial class LanguageChip : UserControl
{
    public static readonly DependencyProperty LanguageCodeProperty =
        DependencyProperty.Register(
            nameof(LanguageCode),
            typeof(string),
            typeof(LanguageChip),
            new PropertyMetadata(string.Empty));

    public string LanguageCode
    {
        get => (string)GetValue(LanguageCodeProperty);
        set => SetValue(LanguageCodeProperty, value);
    }

    public LanguageChip()
    {
        InitializeComponent();
    }
}
