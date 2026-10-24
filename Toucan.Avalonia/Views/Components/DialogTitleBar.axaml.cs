using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Shared dialog chrome. Window behavior stays in the view; dialog commands retain their normal close path.</summary>
public partial class DialogTitleBar : UserControl
{
    public DialogTitleBar() => InitializeComponent();

    internal void Attach(Window window)
    {
        if (PlatformService.IsMacOS)
        {
            TitleContent.Margin = new Thickness(78, 0, 12, 0);
            CaptionButtons.IsVisible = false;
            return;
        }
        WindowDecorationProperties.SetElementRole(DragArea, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(CaptionButtons, WindowDecorationsElementRole.User);
        MinimizeButton.Click += (_, _) => window.WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => window.Close(null);
        void Sync()
        {
            var maximized = window.WindowState == WindowState.Maximized;
            MaximizeGlyph.IsVisible = !maximized;
            RestoreGlyph.IsVisible = maximized;
            var label = Loc.T(maximized ? "Restore" : "Maximize");
            ToolTip.SetTip(MaximizeButton, label);
            AutomationProperties.SetName(MaximizeButton, label);
        }
        Sync();
        window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) Sync(); };
    }
}
