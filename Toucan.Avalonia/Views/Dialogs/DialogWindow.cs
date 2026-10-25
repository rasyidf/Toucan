using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.Views.Components;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Common behavior for modal dialogs: centered on the owner, Escape cancels.</summary>
public class DialogWindow : Window
{
    private Border? _backplate;
    public DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanMinimize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.None];
        MinWidth = 360;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        if (!PlatformService.IsMacOS) WindowDecorations = WindowDecorations.BorderOnly;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == OffScreenMarginProperty && _backplate != null) _backplate.Margin = OffScreenMargin;
        };
        // Resolve through this window so theme dictionaries are available, and keep the
        // surface in sync when the user changes the app or system theme.
        this.Bind(BackgroundProperty, this.GetResourceObservable("ChromeBackgroundBrush"));
    }

    protected override Type StyleKeyOverride => typeof(Window);

    protected override void OnOpened(EventArgs e)
    {
        // A transparent content root lets the owner window show through on Windows. Give every
        // dialog an opaque backplate, including dialogs whose XAML root is a DockPanel or Grid.
        if (Content is Control content && !ReferenceEquals(content, _backplate))
        {
            // Detach the root before moving it under the backplate; Avalonia controls cannot
            // have two visual parents, even briefly.
            Content = null;
            var titleBar = new DialogTitleBar();
            titleBar.Attach(this);
            DockPanel.SetDock(titleBar, Dock.Top);
            var layout = new DockPanel();
            layout.Children.Add(titleBar);
            layout.Children.Add(content);
            var backplate = new Border { Child = layout, Margin = OffScreenMargin };
            backplate.Bind(Border.BackgroundProperty, this.GetObservable(BackgroundProperty));
            _backplate = backplate;
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
