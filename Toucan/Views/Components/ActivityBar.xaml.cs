using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toucan.Views.Components;

public partial class ActivityBar : UserControl
{
    public static readonly DependencyProperty PanelsProperty =
        DependencyProperty.Register(nameof(Panels), typeof(IEnumerable), typeof(ActivityBar));

    public static readonly DependencyProperty ActivateCommandProperty =
        DependencyProperty.Register(nameof(ActivateCommand), typeof(ICommand), typeof(ActivityBar));

    public IEnumerable Panels
    {
        get => (IEnumerable)GetValue(PanelsProperty);
        set => SetValue(PanelsProperty, value);
    }

    public ICommand ActivateCommand
    {
        get => (ICommand)GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    public ActivityBar()
    {
        InitializeComponent();
    }
}
