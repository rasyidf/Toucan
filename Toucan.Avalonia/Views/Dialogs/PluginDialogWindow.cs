using Avalonia.Controls;
using Avalonia.Layout;
using Toucan.Avalonia.Locales;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Hosts a dialog a plugin registered: its content above a close button, with the app's dialog behaviour (Esc closes).</summary>
public sealed class PluginDialogWindow : DialogWindow
{
    public PluginDialogWindow(string title, Control content, double width, double height)
    {
        Title = title;
        Width = width;
        if (height > 0) Height = height;
        else SizeToContent = SizeToContent.Height;
        CanResize = height > 0;

        var close = new Button { Content = Loc.T("OK"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new global::Avalonia.Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close(true);

        var layout = new DockPanel { Margin = new global::Avalonia.Thickness(20) };
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        layout.Children.Add(new ScrollViewer { Content = content });
        Content = layout;
    }
}
