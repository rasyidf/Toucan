using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>A notification as the host keeps it: who sent it and when.</summary>
public sealed record HostNotification(Guid Id, DateTimeOffset Time, string Source, PluginNotification Content);

public sealed class HostNotificationEventArgs(HostNotification notification) : EventArgs
{
    public HostNotification Notification { get; } = notification;
}

/// <summary>
/// Where notifications from plugins (and the host) are collected. The UI shows new ones as they arrive; the list is the
/// notification history.
/// </summary>
public interface INotificationCenter
{
    IReadOnlyList<HostNotification> History { get; }

    event EventHandler<HostNotificationEventArgs>? Notified;

    /// <param name="source">The plugin ID, or <c>toucan</c> for the application itself.</param>
    HostNotification Publish(string source, PluginNotification notification);

    void Clear();
}

public sealed class NotificationCenter(IDiagnosticsService? diagnostics = null) : INotificationCenter
{
    private const int MaxHistory = 200;
    private readonly object _gate = new();
    private readonly List<HostNotification> _history = [];

    public IReadOnlyList<HostNotification> History { get { lock (_gate) return [.. _history]; } }

    public event EventHandler<HostNotificationEventArgs>? Notified;

    public HostNotification Publish(string source, PluginNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        // Plugin text can contain a token it just received; mask it before anything shows or keeps it.
        var safe = diagnostics is null ? notification : notification with
        {
            Title = diagnostics.Redact(notification.Title),
            Message = notification.Message is null ? null : diagnostics.Redact(notification.Message),
        };
        var entry = new HostNotification(Guid.NewGuid(), DateTimeOffset.Now, source, safe);
        lock (_gate)
        {
            _history.Add(entry);
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
        }
        Notified?.Invoke(this, new HostNotificationEventArgs(entry));
        return entry;
    }

    public void Clear()
    {
        lock (_gate) _history.Clear();
    }
}
