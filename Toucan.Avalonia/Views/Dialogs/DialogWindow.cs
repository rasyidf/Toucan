using Avalonia;
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
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        MinWidth = 360;
        // Resolve through this window so theme dictionaries are available, and keep the
        // surface in sync when the user changes the app or system theme.
        this.Bind(BackgroundProperty, this.GetResourceObservable("ChromeBackgroundBrush"));
    }

    protected override Type StyleKeyOverride => typeof(Window);

    protected override void OnOpened(EventArgs e)
    {
        // A transparent content root lets the owner window show through on Windows. Give every
        // dialog an opaque backplate, including dialogs whose XAML root is a DockPanel or Grid.
        if (Content is Control content)
        {
            // Detach the root before moving it under the backplate; Avalonia controls cannot
            // have two visual parents, even briefly.
            Content = null;
            var backplate = new Border { Child = content };
            backplate.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));
            Content = backplate;
        }

        base.OnOpened(e);
    }

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
