using Avalonia;
using Avalonia.Controls;

namespace Toucan.Avalonia.Views;

/// <summary>Distraction-free editor showing one key at a time.</summary>
public partial class ZenEditorView : UserControl
{
    public ZenEditorView() => InitializeComponent();

    /// <summary>
    /// Space reserved on the left of the header for the window controls that macOS draws over the content
    /// (the main window extends into the title bar). Zero elsewhere.
    /// </summary>
    public void SetTitleBarInset(double leading, double trailing = 0) => Header.Padding = new Thickness(Math.Max(16, leading), 0, Math.Max(16, trailing), 0);
}
