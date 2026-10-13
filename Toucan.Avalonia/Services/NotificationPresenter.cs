using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Toucan.Avalonia.Locales;
using Toucan.Core.Commands;
using Toucan.Core.Plugins;
using Toucan.Plugins;

namespace Toucan.Avalonia.Services;

/// <summary>Shows what plugins (and the host) notify as toasts in the main window. Clicking a toast that offers an action runs it.</summary>
internal sealed class NotificationPresenter : IDisposable
{
    private readonly INotificationCenter _center;
    private readonly ICommandRegistry _commands;
    private readonly WindowNotificationManager _manager;

    public NotificationPresenter(Window window, INotificationCenter center, ICommandRegistry commands)
    {
        _center = center;
        _commands = commands;
        _manager = new WindowNotificationManager(window) { Position = NotificationPosition.BottomRight, MaxItems = 4 };
        _center.Notified += OnNotified;
    }

    public void Dispose() => _center.Notified -= OnNotified;

    private void OnNotified(object? sender, HostNotificationEventArgs e)
    {
        // Always after the current layout pass: a toast raised while the window is still being built (a plugin notifying during
        // startup) would otherwise be shown before the notification host is attached and be lost.
        Dispatcher.UIThread.Post(() => Show(e.Notification), DispatcherPriority.Background);
    }

    internal void Show(HostNotification notification)
    {
        var content = notification.Content;
        var message = content.Message ?? string.Empty;
        Action? onClick = null;
        if (content.ActionCommandId is { Length: > 0 } commandId)
        {
            var label = content.ActionLabel is { Length: > 0 } l ? l : Loc.T("Open");
            message = string.IsNullOrEmpty(message) ? label : $"{message}{Environment.NewLine}{label}";
            onClick = () => _ = _commands.ExecuteAsync(commandId);
        }

        var type = content.Severity switch
        {
            NotificationSeverity.Success => NotificationType.Success,
            NotificationSeverity.Warning => NotificationType.Warning,
            NotificationSeverity.Error => NotificationType.Error,
            _ => NotificationType.Information,
        };
        // Errors and sticky notifications stay until dismissed; the rest go away by themselves.
        var expiration = content.Sticky ? TimeSpan.Zero : content.Severity == NotificationSeverity.Error ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(5);
        _manager.Show(new Notification(content.Title, message, type, expiration, onClick));
    }
}
