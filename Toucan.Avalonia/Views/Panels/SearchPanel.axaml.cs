using Avalonia.Controls;

namespace Toucan.Avalonia.Views.Panels;

/// <summary>Find and replace across translation values.</summary>
public partial class SearchPanel : UserControl
{
    public SearchPanel()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => QueryBox.Focus();
    }
}
