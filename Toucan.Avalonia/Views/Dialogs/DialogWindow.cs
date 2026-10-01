using Avalonia.Controls;
using Avalonia.Input;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Common behavior for modal dialogs: centered on the owner, Escape cancels.</summary>
public class DialogWindow : Window
{
    public DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanMinimize = false;
        MinWidth = 360;
        Background = global::Avalonia.Application.Current?.FindResource("ChromeBackgroundBrush") as global::Avalonia.Media.IBrush;
    }

    protected override Type StyleKeyOverride => typeof(Window);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }
}
