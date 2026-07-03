using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toucan.Core.Models;

namespace Toucan.Views.Components;

public partial class ActivityBar : UserControl
{
    public static readonly DependencyProperty PanelsProperty =
        DependencyProperty.Register(nameof(Panels), typeof(IEnumerable), typeof(ActivityBar), new PropertyMetadata(null, OnPanelsChanged));

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

    private static void OnPanelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (ActivityBar)d;
        if (e.OldValue is INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= bar.PanelsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol)
            newCol.CollectionChanged += bar.PanelsCollectionChanged;
        bar.RebuildContextMenu();
    }

    private void PanelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildContextMenu();

    private void RebuildContextMenu()
    {
        PanelVisibilityMenu.Items.Clear();
        if (Panels == null) return;

        foreach (var item in Panels)
        {
            if (item is ISidePanel panel)
            {
                var mi = new MenuItem
                {
                    Header = panel.Title,
                    IsCheckable = true,
                    IsChecked = panel.IsVisible,
                    Tag = panel
                };
                mi.Click += ContextMenuItem_Click;
                PanelVisibilityMenu.Items.Add(mi);
            }
        }
    }

    private void ContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ISidePanel panel)
        {
            panel.IsVisible = mi.IsChecked;
        }
    }
}
