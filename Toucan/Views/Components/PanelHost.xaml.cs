using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toucan.Views.Components;

public partial class PanelHost : UserControl
{
    public static readonly DependencyProperty PanelTitleProperty =
        DependencyProperty.Register(nameof(PanelTitle), typeof(string), typeof(PanelHost), new PropertyMetadata("PANEL"));

    public static readonly DependencyProperty PanelContentProperty =
        DependencyProperty.Register(nameof(PanelContent), typeof(object), typeof(PanelHost));

    public static readonly DependencyProperty HideCommandProperty =
        DependencyProperty.Register(nameof(HideCommand), typeof(ICommand), typeof(PanelHost));

    public string PanelTitle
    {
        get => (string)GetValue(PanelTitleProperty);
        set => SetValue(PanelTitleProperty, value);
    }

    public object PanelContent
    {
        get => GetValue(PanelContentProperty);
        set => SetValue(PanelContentProperty, value);
    }

    public ICommand HideCommand
    {
        get => (ICommand)GetValue(HideCommandProperty);
        set => SetValue(HideCommandProperty, value);
    }

    public PanelHost()
    {
        InitializeComponent();
    }

    private void EllipsisMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            fe.ContextMenu.IsOpen = true;
        }
    }

    private void HidePanel_Click(object sender, RoutedEventArgs e)
    {
        if (HideCommand?.CanExecute(null) == true)
            HideCommand.Execute(null);
    }
}
