using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using Toucan.Plugins.Desktop;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// A status bar item a plugin added. The plugin holds it as <see cref="IStatusBarItem"/> and may change it from any thread;
/// the display is told on the UI thread.
/// </summary>
public sealed partial class PluginStatusBarItem(string pluginId, string id, string title, StatusBarSide side, int order, ICommand? command) : ObservableObject, IStatusBarItem
{
    private string? _text, _icon, _toolTip, _badge;
    private StatusBarSeverity _badgeSeverity;
    private bool _isVisible = true;

    public string PluginId { get; } = pluginId;
    public string Id { get; } = id;

    /// <summary>The accessible name; also the tooltip while the plugin has not set one.</summary>
    public string Title { get; } = title;

    public StatusBarSide Side { get; } = side;
    public int Order { get; } = order;
    public ICommand? Command { get; } = command;

    public string? Text { get => _text; set => Set(ref _text, value); }
    public string? Icon { get => _icon; set => Set(ref _icon, value); }
    public string? ToolTip { get => _toolTip; set => Set(ref _toolTip, value); }
    public string? Badge { get => _badge; set => Set(ref _badge, value); }
    public StatusBarSeverity BadgeSeverity { get => _badgeSeverity; set => Set(ref _badgeSeverity, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    // What the status bar binds to: derived from the values above.
    public FASymbol IconSymbol => ParseIcon() ?? FASymbol.Document;
    public bool HasIcon => ParseIcon() is not null;
    public bool HasText => !string.IsNullOrEmpty(_text);
    public bool HasBadge => !string.IsNullOrEmpty(_badge);
    public bool IsClickable => Command is not null;
    public string DisplayToolTip => string.IsNullOrEmpty(_toolTip) ? Title : _toolTip;
    public bool IsBadgeGood => _badgeSeverity == StatusBarSeverity.Success;
    public bool IsBadgeWarn => _badgeSeverity == StatusBarSeverity.Warning;
    public bool IsBadgeBad => _badgeSeverity == StatusBarSeverity.Error;

    /// <summary>Shown only when it has something to show, so an empty item takes no room.</summary>
    public bool IsShown => _isVisible && (HasText || HasIcon || HasBadge);

    private FASymbol? ParseIcon() => _icon is not null && Enum.TryParse<FASymbol>(_icon, ignoreCase: true, out var symbol) ? symbol : null;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (Dispatcher.UIThread.CheckAccess()) Raise(name);
        else Dispatcher.UIThread.Post(() => Raise(name));
    }

    private void Raise(string? name)
    {
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(IconSymbol));
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(DisplayToolTip));
        OnPropertyChanged(nameof(IsBadgeGood));
        OnPropertyChanged(nameof(IsBadgeWarn));
        OnPropertyChanged(nameof(IsBadgeBad));
        OnPropertyChanged(nameof(IsShown));
    }
}
