using System.Collections;
using Avalonia.Controls;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Vertical strip of panel icons (VS Code-style). Clicking toggles or activates a side panel.</summary>
public partial class ActivityBar : UserControl
{
    public ActivityBar()
    {
        InitializeComponent();
    }

    public IEnumerable? Panels
    {
        get => PanelItems.ItemsSource;
        set => PanelItems.ItemsSource = value;
    }
}
